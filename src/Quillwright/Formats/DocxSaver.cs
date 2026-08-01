using Quillwright.Diagnostics;
using Quillwright.IO;
using Quillwright.Model;
using Quillwright.Styles;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>
/// Writes a document as an Open Packaging Conventions package.
/// </summary>
/// <remarks>
/// Saving is copy-on-write at the package level: parts the model owns are regenerated from
/// the model, and every other part of a loaded package — themes, charts, embedded objects,
/// custom XML, the VBA project — is copied through byte for byte along with its
/// relationships and content type.
/// </remarks>
internal static partial class DocxSaver
{
    /// <summary>Writes the document into a stream as a package.</summary>
    public static async ValueTask SaveAsync(WordDocument document, Stream stream, SaveOptions options, CancellationToken cancellationToken)
    {
        var plan = new SavePlan(document);
        plan.Prepare();

        OpcPackage package = await OpcPackage.CreateAsync(stream, leaveOpen: true, cancellationToken).ConfigureAwait(false);
        await using (package.ConfigureAwait(false))
        {
            PreservedPackage? preserved = options.WritePreservedContent ? document.Preserved : null;
            if (preserved is not null)
                package.MergeContentTypes(preserved.ContentTypes);

            // A Strict package keeps its vocabulary as well as its namespace: the parts that
            // are copied through still speak Strict, so the ones regenerated here have to.
            package.Strict = document.Preserved?.IsStrict == true;

            WriteRootRelationships(package, plan, preserved);

            await WriteMainPartAsync(package, document, plan, cancellationToken).ConfigureAwait(false);
            await WriteStylesAsync(package, document, plan, cancellationToken).ConfigureAwait(false);
            await WriteSettingsAsync(package, document, plan, cancellationToken).ConfigureAwait(false);
            await WriteNumberingAsync(package, document, plan, cancellationToken).ConfigureAwait(false);
            await WriteNotesAsync(package, document, plan, cancellationToken).ConfigureAwait(false);
            // Every part that describes comments keys off an identifier that also appears
            // somewhere else — on the comment's own last paragraph, or in the sibling part —
            // so all of them are settled before the first of the four is written.
            Dictionary<Comment, string> threads = CommentThreadWriter.Prepare(document, plan.WritesCommentIds);
            Dictionary<Comment, string> durable = CommentIdsPart.Prepare(document, threads);
            await WriteCommentsAsync(package, document, plan, cancellationToken).ConfigureAwait(false);
            await WriteCommentThreadsAsync(package, document, plan, threads, cancellationToken).ConfigureAwait(false);
            await WriteCommentIdsAsync(package, document, plan, threads, durable, cancellationToken).ConfigureAwait(false);
            await WriteCommentsExtensibleAsync(package, document, plan, durable, cancellationToken).ConfigureAwait(false);
            await WritePeopleAsync(package, document, plan, cancellationToken).ConfigureAwait(false);
            await WriteHeadersAndFootersAsync(package, document, plan, cancellationToken).ConfigureAwait(false);
            await WriteMediaAsync(package, plan, cancellationToken).ConfigureAwait(false);
            await WritePropertiesAsync(package, document, plan, options, cancellationToken).ConfigureAwait(false);

            // The parts that declare relationships of their own have all been written by now,
            // so what they asked for while being written is known.
            foreach (OpcRelationship relationship in plan.MainRelationships)
                package.AddRelationship(plan.MainPartPath, relationship);

            foreach ((string source, List<OpcRelationship> relationships) in plan.PartRelationships)
            {
                foreach (OpcRelationship relationship in relationships)
                    package.AddRelationship(source, relationship);
            }

            await CopyPreservedAsync(package, plan, preserved, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void WriteRootRelationships(OpcPackage package, SavePlan plan, PreservedPackage? preserved)
    {
        List<OpcRelationship> root =
            preserved is not null && preserved.Relationships.TryGetValue("/", out List<OpcRelationship>? existing) && existing.Count > 0
                ? [.. existing]
                :
                [
                    new OpcRelationship("rId1", DocxSchema.RelDocument, OpcPath.ToEntryName(plan.MainPartPath)),
                    new OpcRelationship("rId2", DocxSchema.RelCoreProperties, "docProps/core.xml"),
                ];

        // A document that gained its first custom property also needs the relationship that
        // makes the new part reachable; one that already had the part keeps its own id.
        var ids = new RelationshipIdAllocator();
        ids.Reserve(root);
        Declare(root, ids, plan, plan.WritesApplicationProperties, DocxSchema.RelExtendedProperties, "docProps/app.xml");
        Declare(root, ids, plan, plan.WritesCustomProperties, DocxSchema.RelCustomProperties, "docProps/custom.xml");

        foreach (OpcRelationship relationship in root)
            package.AddRelationship("/", relationship);
    }

    private static void Declare(
        List<OpcRelationship> root, RelationshipIdAllocator ids, SavePlan plan, bool writes, string type, string target)
    {
        if (writes && !root.Any(r => r.Is(type)))
            root.Add(new OpcRelationship(ids.Next(), plan.Spell(type), target));
    }

    private static async ValueTask WriteMainPartAsync(OpcPackage package, WordDocument document, SavePlan plan, CancellationToken cancellationToken)
    {
        Utf8XmlWriter writer = await package.CreateXmlPartAsync(plan.MainPartPath, plan.MainContentType, cancellationToken).ConfigureAwait(false);
        await using (writer.ConfigureAwait(false))
        {
            WordXml.OpenRoot(writer, "document"u8, document.RootAttributes);
            RawXml.Write(writer, document.BackgroundXml);
            writer.WriteRaw("<w:body>"u8);

            BodyWriteContext context = CreateContext(plan, document);
            for (int i = 0; i < document.Sections.Count; i++)
            {
                Section section = document.Sections[i];
                EnsureTerminalParagraph(section, isLast: i == document.Sections.Count - 1);
                BodyWriter.WriteBlocks(writer, section.Blocks, context);
                await writer.FlushIfNeededAsync(cancellationToken).ConfigureAwait(false);
            }

            // The properties of the last section live at the end of the body rather than
            // inside a paragraph, which is what marks it as the final one.
            SectionWriter.Write(writer, document.Sections.Last.Properties, BuildSectionContext(document.Sections.Last, plan));
            writer.WriteRaw("</w:body></w:document>"u8);
        }
    }

    /// <summary>
    /// Every section but the last ends at a paragraph carrying its properties, so a section
    /// that ends with a table gets an empty paragraph to carry the break.
    /// </summary>
    private static void EnsureTerminalParagraph(Section section, bool isLast)
    {
        foreach (Block block in section.Blocks)
        {
            if (block is Paragraph paragraph)
                paragraph.SectionBreak = null;
        }

        if (isLast)
            return;

        if (section.Blocks.Count == 0 || section.Blocks[^1] is not Paragraph last)
        {
            last = new Paragraph();
            section.Blocks.Add(last);
        }

        last.SectionBreak = section.Properties;
    }

    private static BodyWriteContext CreateContext(SavePlan plan, WordDocument document) => new()
    {
        ResolvePicture = picture => plan.RelationshipFor(picture, plan.MainPartPath),
        ResolveHyperlink = link => plan.RelationshipFor(link, plan.MainPartPath),
        SectionBreakAt = paragraph => paragraph.SectionBreak is { } properties
            ? (properties, BuildSectionContext(FindSection(document, properties), plan))
            : null,
    };

    /// <summary>
    /// The same, for a part that declares its own relationships. A picture in a header is
    /// pointed at from the header's relationships, so it needs an id the header allocated
    /// rather than the one the document part would have used.
    /// </summary>
    private static BodyWriteContext CreateContext(SavePlan plan, string partPath) => new()
    {
        ResolvePicture = picture => plan.RelationshipFor(picture, partPath),
        ResolveHyperlink = link => plan.RelationshipFor(link, partPath),
    };

    private static Section FindSection(WordDocument document, SectionProperties properties) =>
        document.Sections.FirstOrDefault(section => ReferenceEquals(section.Properties, properties)) ?? document.Sections.Last;

    private static SectionWriteContext BuildSectionContext(Section section, SavePlan plan)
    {
        var context = new SectionWriteContext();
        foreach ((HeaderFooterKind kind, HeaderFooter content) in section.Headers.Defined)
            context.References.Add((false, kind, plan.RelationshipFor(content)));
        foreach ((HeaderFooterKind kind, HeaderFooter content) in section.Footers.Defined)
            context.References.Add((true, kind, plan.RelationshipFor(content)));
        return context;
    }

    private static async ValueTask WriteStylesAsync(OpcPackage package, WordDocument document, SavePlan plan, CancellationToken cancellationToken)
    {
        string path = plan.PathFor(DocxSchema.RelStyles, DocxSchema.PartStyles);
        Utf8XmlWriter writer = await package.CreateXmlPartAsync(path, DocxSchema.ContentTypeStyles, cancellationToken).ConfigureAwait(false);
        await using (writer.ConfigureAwait(false))
            StylesPartWriter.Write(writer, document.Styles);
    }

    private static async ValueTask WriteSettingsAsync(OpcPackage package, WordDocument document, SavePlan plan, CancellationToken cancellationToken)
    {
        string path = plan.PathFor(DocxSchema.RelSettings, DocxSchema.PartSettings);
        Utf8XmlWriter writer = await package.CreateXmlPartAsync(path, DocxSchema.ContentTypeSettings, cancellationToken).ConfigureAwait(false);
        await using (writer.ConfigureAwait(false))
        {
            WordXml.OpenRoot(writer, "settings"u8, document.Settings.Attributes);
            foreach ((_, string xml) in document.Settings.Elements)
                writer.WriteRawXml(xml);
            writer.WriteRaw("</w:settings>"u8);
        }
    }

    private static async ValueTask WriteNumberingAsync(OpcPackage package, WordDocument document, SavePlan plan, CancellationToken cancellationToken)
    {
        if (!plan.Writes(DocxSchema.RelNumbering, DocxSchema.PartNumbering, !document.Numbering.IsEmpty))
            return;

        string path = plan.PathFor(DocxSchema.RelNumbering, DocxSchema.PartNumbering);
        Utf8XmlWriter writer = await package.CreateXmlPartAsync(path, DocxSchema.ContentTypeNumbering, cancellationToken).ConfigureAwait(false);
        await using (writer.ConfigureAwait(false))
            NumberingPartWriter.Write(writer, document.Numbering);
    }

    private static async ValueTask WriteNotesAsync(OpcPackage package, WordDocument document, SavePlan plan, CancellationToken cancellationToken)
    {
        await WriteNoteSetAsync(package, document, plan, document.Footnotes, isEndnote: false, cancellationToken).ConfigureAwait(false);
        await WriteNoteSetAsync(package, document, plan, document.Endnotes, isEndnote: true, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask WriteNoteSetAsync(
        OpcPackage package, WordDocument document, SavePlan plan, IReadOnlyList<Note> notes, bool isEndnote, CancellationToken cancellationToken)
    {
        string relationshipType = isEndnote ? DocxSchema.RelEndnotes : DocxSchema.RelFootnotes;
        string fallback = isEndnote ? DocxSchema.PartEndnotes : DocxSchema.PartFootnotes;
        if (!plan.Writes(relationshipType, fallback, notes.Count > 0))
            return;

        string partPath = plan.PathFor(relationshipType, fallback);
        string contentType = isEndnote ? DocxSchema.ContentTypeEndnotes : DocxSchema.ContentTypeFootnotes;

        document.PartRoots.TryGetValue(isEndnote ? "endnotes" : "footnotes", out string? rootAttributes);
        Utf8XmlWriter writer = await package.CreateXmlPartAsync(partPath, contentType, cancellationToken).ConfigureAwait(false);
        await using (writer.ConfigureAwait(false))
            WriteNoteBody(writer, notes, isEndnote, rootAttributes, CreateContext(plan, partPath));
    }

    private static void WriteNoteBody(
        Utf8XmlWriter writer, IReadOnlyList<Note> notes, bool isEndnote, string? rootAttributes, BodyWriteContext context)
    {
        ReadOnlySpan<byte> root = isEndnote ? "endnotes"u8 : "footnotes"u8;
        ReadOnlySpan<byte> item = isEndnote ? "endnote"u8 : "footnote"u8;

        WordXml.OpenRoot(writer, root, rootAttributes);
        foreach (Note note in notes)
        {
            WordXml.Open(writer, item);
            if (note.Kind != NoteKind.Normal)
            {
                WordXml.Attribute(writer, "w:type"u8, note.Kind switch
                {
                    NoteKind.Separator => "separator",
                    NoteKind.ContinuationSeparator => "continuationSeparator",
                    _ => "continuationNotice",
                });
            }

            WordXml.Attribute(writer, "w:id"u8, note.Id);
            writer.WriteRaw(">"u8);
            BodyWriter.WriteBlocks(writer, note.Blocks, context);
            WordXml.Close(writer, item);
        }

        WordXml.Close(writer, root);
    }

    private static async ValueTask WriteHeadersAndFootersAsync(OpcPackage package, WordDocument document, SavePlan plan, CancellationToken cancellationToken)
    {
        foreach (HeaderFooter part in document.HeaderFooters)
        {
            if (part.PartPath is not { } path)
                continue;

            string contentType = part.IsFooter ? DocxSchema.ContentTypeFooter : DocxSchema.ContentTypeHeader;
            Utf8XmlWriter writer = await package.CreateXmlPartAsync(path, contentType, cancellationToken).ConfigureAwait(false);
            await using (writer.ConfigureAwait(false))
                WriteHeaderFooterBody(writer, part, CreateContext(plan, path));
        }
    }

    private static void WriteHeaderFooterBody(Utf8XmlWriter writer, HeaderFooter part, BodyWriteContext context)
    {
        ReadOnlySpan<byte> root = part.IsFooter ? "ftr"u8 : "hdr"u8;
        WordXml.OpenRoot(writer, root, part.Attributes);
        if (part.Blocks.Count == 0)
            part.Blocks.Add(new Paragraph());
        BodyWriter.WriteBlocks(writer, part.Blocks, context);
        WordXml.Close(writer, root);
    }

    private static async ValueTask WriteMediaAsync(OpcPackage package, SavePlan plan, CancellationToken cancellationToken)
    {
        foreach ((string path, ImageData image) in plan.NewMedia)
        {
            package.RegisterDefaultContentType(image.Extension, image.ContentType);
            await package.WriteRawPartAsync(path, image.Bytes.ToArray(), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask WritePropertiesAsync(
        OpcPackage package, WordDocument document, SavePlan plan, SaveOptions options, CancellationToken cancellationToken)
    {
        Utf8XmlWriter core = await package.CreateXmlPartAsync(
            DocxSchema.PartCoreProperties, DocxSchema.ContentTypeCoreProperties, cancellationToken).ConfigureAwait(false);
        await using (core.ConfigureAwait(false))
            CorePropertiesWriter.Write(core, document.Properties, options);

        if (plan.WritesApplicationProperties)
        {
            Utf8XmlWriter application = await package.CreateXmlPartAsync(
                DocxSchema.PartExtendedProperties, DocxSchema.ContentTypeExtendedProperties, cancellationToken).ConfigureAwait(false);
            await using (application.ConfigureAwait(false))
                ExtendedPropertiesPart.Write(application, document.ApplicationProperties);
        }

        if (!plan.WritesCustomProperties)
            return;

        Utf8XmlWriter custom = await package.CreateXmlPartAsync(
            DocxSchema.PartCustomProperties, DocxSchema.ContentTypeCustomProperties, cancellationToken).ConfigureAwait(false);
        await using (custom.ConfigureAwait(false))
            CustomPropertiesPart.Write(custom, document.CustomProperties);
    }

    private static async ValueTask CopyPreservedAsync(OpcPackage package, SavePlan plan, PreservedPackage? preserved, CancellationToken cancellationToken)
    {
        if (preserved is null)
            return;

        foreach ((string path, byte[] content) in preserved.Parts)
        {
            if (plan.RegeneratedParts.Contains(path) || package.WasWritten(path))
                continue;

            if (preserved.ContentTypes.GetContentType(path) is { } contentType)
                package.RegisterContentType(path, contentType);
            await package.WriteRawPartAsync(path, content, cancellationToken).ConfigureAwait(false);
        }

        foreach ((string source, List<OpcRelationship> relationships) in preserved.Relationships)
        {
            if (source == "/" || source == plan.MainPartPath)
                continue;
            foreach (OpcRelationship relationship in relationships)
                package.AddRelationship(source, relationship);
        }
    }
}
