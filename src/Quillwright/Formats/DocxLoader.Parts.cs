using System.Xml;
using Quillwright.IO;
using Quillwright.Model;

namespace Quillwright.Formats;

internal static partial class DocxLoader
{
    private static ValueTask ReadMainPartAsync(OpcPackage package, LoadContext context, BodyReader body, CancellationToken cancellationToken) =>
        ReadPartAsync(package, context.Preserved.MainPartPath, context, cancellationToken, xml =>
        {
            StylesPartReader.MoveToRoot(xml, "document");
            if (xml.NodeType != XmlNodeType.Element)
                return;

            context.Document.RootAttributes = XmlHelp.CaptureRootAttributes(xml);
            XmlHelp.ForEachChild(xml, (child, childName) =>
            {
                switch (childName)
                {
                    case "background":
                        context.Document.BackgroundXml = child.ReadOuterXml();
                        return;
                    case "body":
                        ReadBody(child, context, body);
                        return;
                    default:
                        child.Skip();
                        return;
                }
            });
        });

    private static void ReadBody(XmlReader xml, LoadContext context, BodyReader body)
    {
        var flat = new BodyBuffer(context);
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "p":
                    flat.Add(body.ReadParagraph(reader));
                    return;
                case "tbl":
                    flat.Add(body.ReadTable(reader));
                    return;
                case "sdt":
                case "AlternateContent" when reader.NamespaceURI == DocxSchema.NsMarkupCompatibility:
                    flat.AddElement(reader, name, body);
                    return;
                case "sectPr":
                    flat.SetFinalSection(SectionReader.Read(reader, out List<SectionReader.Reference> references), references);
                    return;
                default:
                    flat.Add(new RawBlock(reader.ReadOuterXml()));
                    return;
            }
        });

        flat.Build();
    }

    private static ValueTask ReadNotesAsync(
        OpcPackage package, LoadContext context, BodyReader body, bool isEndnote, CancellationToken cancellationToken)
    {
        string partPath = context.Preserved.PathFor(
            isEndnote ? DocxSchema.RelEndnotes : DocxSchema.RelFootnotes,
            isEndnote ? DocxSchema.PartEndnotes : DocxSchema.PartFootnotes);
        string root = isEndnote ? "endnotes" : "footnotes";
        string item = isEndnote ? "endnote" : "footnote";
        List<Note> target = isEndnote ? context.Document.EndnoteList : context.Document.FootnoteList;

        return ReadPartAsync(package, partPath, context, cancellationToken, xml =>
        {
            StylesPartReader.MoveToRoot(xml, root);
            if (xml.NodeType != XmlNodeType.Element)
                return;

            context.Document.PartRoots[root] = XmlHelp.CaptureRootAttributes(xml);
            XmlHelp.ForEachChild(xml, (reader, name) =>
            {
                if (name != item)
                {
                    reader.Skip();
                    return;
                }

                var note = new Note(context.Document, isEndnote)
                {
                    Id = XmlHelp.AttrInt(reader, "id") ?? 0,
                    Kind = XmlHelp.Attr(reader, "type") switch
                    {
                        "separator" => NoteKind.Separator,
                        "continuationSeparator" => NoteKind.ContinuationSeparator,
                        "continuationNotice" => NoteKind.ContinuationNotice,
                        _ => NoteKind.Normal,
                    },
                };

                body.ReadBlocks(reader, note);
                target.Add(note);
            });
        });
    }

    private static ValueTask ReadCommentsAsync(OpcPackage package, LoadContext context, BodyReader body, CancellationToken cancellationToken) =>
        ReadPartAsync(package, context.Preserved.PathFor(DocxSchema.RelComments, DocxSchema.PartComments), context, cancellationToken, xml =>
        {
            StylesPartReader.MoveToRoot(xml, "comments");
            if (xml.NodeType != XmlNodeType.Element)
                return;

            context.Document.PartRoots["comments"] = XmlHelp.CaptureRootAttributes(xml);
            XmlHelp.ForEachChild(xml, (reader, name) =>
            {
                if (name != "comment")
                {
                    reader.Skip();
                    return;
                }

                var comment = new Comment(context.Document)
                {
                    Id = XmlHelp.AttrInt(reader, "id") ?? 0,
                    Author = XmlHelp.Attr(reader, "author"),
                    Initials = XmlHelp.Attr(reader, "initials"),
                    Date = XmlHelp.ParseDate(XmlHelp.Attr(reader, "date")),
                    Attributes = XmlHelp.CaptureAttributes(reader, "id", "author", "initials", "date"),
                };

                body.ReadBlocks(reader, comment);
                context.Document.CommentList.Add(comment);
            });
        });

    private static async ValueTask ReadHeadersAndFootersAsync(
        OpcPackage package,
        LoadContext context,
        BodyReader body,
        Dictionary<string, string> partsByRelationship,
        CancellationToken cancellationToken)
    {
        foreach ((string relationshipId, string partPath) in partsByRelationship)
        {
            bool isFooter = partPath.Contains("footer", StringComparison.OrdinalIgnoreCase);
            var part = new HeaderFooter(context.Document, isFooter)
            {
                PartPath = partPath,
                RelationshipId = relationshipId,
            };

            await ReadPartAsync(package, partPath, context, cancellationToken, xml =>
            {
                StylesPartReader.MoveToRoot(xml, isFooter ? "ftr" : "hdr");
                if (xml.NodeType != XmlNodeType.Element)
                    return;

                part.Attributes = XmlHelp.CaptureRootAttributes(xml);
                body.ReadBlocks(xml, part);
            }).ConfigureAwait(false);

            context.Document.RegisterHeaderFooter(part);
            context.HeadersByRelationship[relationshipId] = part;
        }
    }
}
