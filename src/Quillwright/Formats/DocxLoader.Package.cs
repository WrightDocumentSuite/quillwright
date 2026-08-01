using System.Xml;
using Quillwright.Diagnostics;
using Quillwright.IO;
using Quillwright.Model;
using Quillwright.Xml;

namespace Quillwright.Formats;

internal static partial class DocxLoader
{
    private static async ValueTask<WordDocument> ReadAsync(Stream stream, LoadOptions options, CancellationToken cancellationToken)
    {
        OpcPackage package = await OpcPackage
            .OpenReadAsync(stream, leaveOpen: true, cancellationToken, options.Password)
            .ConfigureAwait(false);
        await using (package.ConfigureAwait(false))
        {
            return await ReadPackageAsync(package, options, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask<WordDocument> ReadPackageAsync(OpcPackage package, LoadOptions options, CancellationToken cancellationToken)
    {
        WordDocument document = WordDocument.CreateEmpty();
        var preserved = new PreservedPackage
        {
            ContentTypes = await package.GetContentTypesAsync(cancellationToken).ConfigureAwait(false),
        };

        document.Preserved = preserved;
        var context = new LoadContext(document, options, preserved);

        await ReadRelationshipsAsync(package, preserved, cancellationToken).ConfigureAwait(false);
        preserved.IsStrict = preserved.Relationships.TryGetValue("/", out List<OpcRelationship>? rootRelationships) &&
            rootRelationships.Any(static r => r.Type.StartsWith(DocxSchema.NsRelationshipsStrict, StringComparison.Ordinal));
        preserved.MainPartPath = FindMainPart(preserved)
            ?? (package.PartExists(DocxSchema.PartDocument)
                ? DocxSchema.PartDocument
                : throw new DocxFormatException("The package has no main document part."));
        preserved.MainContentType = preserved.ContentTypes.GetContentType(preserved.MainPartPath) ?? DocxSchema.ContentTypeDocument;

        var modelled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            preserved.MainPartPath,
            preserved.PathFor(DocxSchema.RelStyles, DocxSchema.PartStyles),
            preserved.PathFor(DocxSchema.RelNumbering, DocxSchema.PartNumbering),
            preserved.PathFor(DocxSchema.RelSettings, DocxSchema.PartSettings),
            preserved.PathFor(DocxSchema.RelFootnotes, DocxSchema.PartFootnotes),
            preserved.PathFor(DocxSchema.RelEndnotes, DocxSchema.PartEndnotes),
            preserved.PathFor(DocxSchema.RelComments, DocxSchema.PartComments),
            DocxSchema.PartCoreProperties,
            DocxSchema.PartExtendedProperties,
            DocxSchema.PartCustomProperties,
        };

        Dictionary<string, string> headerFooterParts = CollectHeaderFooterParts(preserved, modelled);
        foreach (string path in modelled)
        {
            if (package.PartExists(path))
                preserved.PresentModelledParts.Add(path);
        }

        await CapturePartsAsync(package, preserved, modelled, cancellationToken).ConfigureAwait(false);
        await LoadMediaAsync(package, context, modelled, cancellationToken).ConfigureAwait(false);
        document.Macros = ReadMacros(preserved, context);

        // Signatures are checked against the parts as bytes, so they have to be read while the
        // package is open and before the parts this models stop being bytes.
        document.SignatureList.AddRange(
            await SignatureReader.ReadAsync(package, preserved, cancellationToken).ConfigureAwait(false));

        await ReadPartAsync(package, preserved.PathFor(DocxSchema.RelStyles, DocxSchema.PartStyles), context, cancellationToken,
            xml => document.Styles = StylesPartReader.Read(xml, context)).ConfigureAwait(false);
        await ReadPartAsync(package, preserved.PathFor(DocxSchema.RelNumbering, DocxSchema.PartNumbering), context, cancellationToken,
            xml => NumberingPartReader.Read(xml, document.Numbering, context)).ConfigureAwait(false);
        await ReadPartAsync(package, preserved.PathFor(DocxSchema.RelSettings, DocxSchema.PartSettings), context, cancellationToken,
            xml => SettingsPartReader.Read(xml, document.Settings)).ConfigureAwait(false);
        await ReadPartAsync(package, DocxSchema.PartCoreProperties, context, cancellationToken,
            xml => document.Properties = CorePropertiesReader.Read(xml)).ConfigureAwait(false);
        await ReadPartAsync(package, DocxSchema.PartExtendedProperties, context, cancellationToken,
            xml => ExtendedPropertiesPart.Read(xml, document.ApplicationProperties)).ConfigureAwait(false);
        await ReadPartAsync(package, DocxSchema.PartCustomProperties, context, cancellationToken,
            xml => CustomPropertiesPart.Read(xml, document.CustomProperties)).ConfigureAwait(false);
        ReadCharts(document, preserved);
        document.WebExtensionList.AddRange(WebExtensionReader.Read(preserved));

        // The theme part is carried through untouched, so its colours are read out of the
        // bytes that were captured rather than parsed again from the package.
        document.Theme = ThemeReader.Read(
            preserved.Parts.GetValueOrDefault(preserved.PathFor(DocxSchema.RelTheme, DocxSchema.PartTheme)),
            document.Settings.GetRaw("clrSchemeMapping"));

        var body = new BodyReader(context);
        await ReadNotesAsync(package, context, body, isEndnote: false, cancellationToken).ConfigureAwait(false);
        await ReadNotesAsync(package, context, body, isEndnote: true, cancellationToken).ConfigureAwait(false);
        await ReadCommentsAsync(package, context, body, cancellationToken).ConfigureAwait(false);
        if (CommentThreadReader.FindPart(preserved) is { } threadPart)
        {
            await ReadPartAsync(package, threadPart, context, cancellationToken,
                xml => CommentThreadReader.Apply(xml, document)).ConfigureAwait(false);
        }

        // The durable identifiers have to be in place before the extended metadata is read,
        // because that part names comments by them and by nothing else.
        if (CommentIdsPart.FindPart(preserved) is { } durablePart)
        {
            await ReadPartAsync(package, durablePart, context, cancellationToken,
                xml => CommentIdsPart.Read(xml, document)).ConfigureAwait(false);
        }

        if (CommentExtensiblePart.FindPart(preserved) is { } extensiblePart)
        {
            await ReadPartAsync(package, extensiblePart, context, cancellationToken,
                xml => CommentExtensiblePart.Read(xml, document)).ConfigureAwait(false);
        }

        if (PeoplePart.FindPart(preserved) is { } peoplePart)
        {
            await ReadPartAsync(package, peoplePart, context, cancellationToken,
                xml => PeoplePart.Read(xml, document)).ConfigureAwait(false);
        }

        await ReadHeadersAndFootersAsync(package, context, body, headerFooterParts, cancellationToken).ConfigureAwait(false);
        await ReadMainPartAsync(package, context, body, cancellationToken).ConfigureAwait(false);

        if (document.Styles.Count == 0)
            document.Styles = Styles.StyleSheet.CreateDefault();
        return document;
    }

    /// <summary>
    /// Reads every chart the package holds, found by content type rather than by walking the
    /// drawings that reference them: a chart in a header is as much a chart as one in the body.
    /// </summary>
    private static void ReadCharts(WordDocument document, PreservedPackage preserved)
    {
        foreach ((string path, byte[] content) in preserved.Parts.OrderBy(static part => part.Key, StringComparer.Ordinal))
        {
            if (preserved.ContentTypes.GetContentType(path) != DocxSchema.ContentTypeChart)
                continue;

            try
            {
                using var xml = XmlReader.Create(new MemoryStream(content), XmlDefaults.ReaderSettings);
                document.ChartList.Add(ChartPartReader.Read(xml, path));
            }
            catch (XmlException)
            {
                // A chart that will not parse is still copied through; it simply has no API.
            }
        }
    }

    private static async ValueTask ReadRelationshipsAsync(OpcPackage package, PreservedPackage preserved, CancellationToken cancellationToken)
    {
        foreach (string partPath in package.PartPaths.ToArray())
        {
            if (!OpcPath.IsRelsPath(partPath))
                continue;

            string source = OpcPath.GetSourcePart(partPath);
            IReadOnlyList<OpcRelationship> relationships = await package.GetRelationshipsAsync(source, cancellationToken).ConfigureAwait(false);
            if (relationships.Count > 0)
                preserved.Relationships[source] = [.. relationships];
        }
    }

    private static string? FindMainPart(PreservedPackage preserved)
    {
        if (!preserved.Relationships.TryGetValue("/", out List<OpcRelationship>? root))
            return null;

        OpcRelationship main = root.FirstOrDefault(r => r.Is(DocxSchema.RelDocument));
        return main.Target is null ? null : OpcPath.Resolve("/", main.Target);
    }

    private static Dictionary<string, string> CollectHeaderFooterParts(PreservedPackage preserved, HashSet<string> modelled)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (OpcRelationship relationship in preserved.MainRelationships)
        {
            if ((!relationship.Is(DocxSchema.RelHeader) && !relationship.Is(DocxSchema.RelFooter)) || relationship.IsExternal)
                continue;

            string path = OpcPath.Resolve(preserved.MainPartPath, relationship.Target);
            result[relationship.Id] = path;
            modelled.Add(path);
        }

        return result;
    }

    private static async ValueTask CapturePartsAsync(
        OpcPackage package, PreservedPackage preserved, HashSet<string> modelled, CancellationToken cancellationToken)
    {
        foreach (string partPath in package.PartPaths.ToArray())
        {
            if (modelled.Contains(partPath) || OpcPath.IsRelsPath(partPath) || partPath == "/[Content_Types].xml")
                continue;

            preserved.Parts[partPath] = await package.ReadPartBytesAsync(partPath, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Decodes the VBA project, which is a compound file of its own carried as an opaque part.
    /// </summary>
    /// <param name="preserved">The parts read from the package.</param>
    /// <param name="context">Where a project that will not decode is reported.</param>
    private static Vba.VbaProject? ReadMacros(PreservedPackage preserved, LoadContext context)
    {
        OpcRelationship relationship = preserved.MainRelationships.FirstOrDefault(static r => r.Is(DocxSchema.RelVbaProject));
        if (relationship.Target is null || relationship.IsExternal)
            return null;

        string path = OpcPath.Resolve(preserved.MainPartPath, relationship.Target);
        if (!preserved.Parts.TryGetValue(path, out byte[]? bytes) || !CompoundFile.IsCompoundFile(bytes))
            return null;

        try
        {
            return Vba.VbaProject.Read(CompoundFile.Open(bytes), string.Empty);
        }
        catch (Exception error) when (error is CompoundFileException or ArgumentException or IndexOutOfRangeException)
        {
            context.Warn(WarningCode.UnreadablePart, $"The VBA project in '{path}' could not be read: {error.Message}");
            return null;
        }
    }

    private static async ValueTask LoadMediaAsync(
        OpcPackage package,
        LoadContext context,
        IReadOnlySet<string> modelledParts,
        CancellationToken cancellationToken)
    {
        PreservedPackage preserved = context.Preserved;
        var imagesByPart = new Dictionary<string, ImageData>(StringComparer.OrdinalIgnoreCase);
        foreach (string sourcePart in modelledParts)
        {
            if (!preserved.Relationships.TryGetValue(sourcePart, out List<OpcRelationship>? relationships))
                continue;

            foreach (OpcRelationship relationship in relationships)
            {
                if (!relationship.Is(DocxSchema.RelImage) || relationship.IsExternal)
                    continue;

                string path = OpcPath.Resolve(sourcePart, relationship.Target);
                if (!package.PartExists(path))
                {
                    string? previous = context.CurrentPart;
                    context.CurrentPart = sourcePart;
                    context.Warn(WarningCode.MissingPart, $"Image part '{path}' is missing.");
                    context.CurrentPart = previous;
                    continue;
                }

                if (!imagesByPart.TryGetValue(path, out ImageData? image))
                {
                    byte[] bytes = context.Options.LoadMedia
                        ? await package.ReadPartBytesAsync(path, cancellationToken).ConfigureAwait(false)
                        : [];

                    image = ImageData.FromBytes(bytes, preserved.ContentTypes.GetContentType(path));
                    image.PartPath = path;
                    imagesByPart[path] = image;
                    context.Document.Media.Add(image);
                }

                // RelationshipId predates part-scoped resolution and remains the main part's
                // id for API compatibility. A header's rId is meaningful only inside it.
                if (sourcePart.Equals(preserved.MainPartPath, StringComparison.OrdinalIgnoreCase))
                    image.RelationshipId = relationship.Id;
                context.RegisterImage(sourcePart, relationship.Id, image);
            }
        }
    }

    private static async ValueTask ReadPartAsync(
        OpcPackage package, string partPath, LoadContext context, CancellationToken cancellationToken, Action<XmlReader> read)
    {
        if (!package.PartExists(partPath))
            return;

        string? previous = context.CurrentPart;
        context.CurrentPart = partPath;
        try
        {
            using MemoryStream buffered = await package.ReadPartAsync(partPath, cancellationToken).ConfigureAwait(false);
            using XmlReader xml = XmlReader.Create(buffered, XmlDefaults.ReaderSettings);
            read(xml);
        }
        catch (XmlException exception)
        {
            context.Warn(WarningCode.UnreadablePart, $"The part could not be parsed: {exception.Message}");
        }
        finally
        {
            context.CurrentPart = previous;
        }
    }
}
