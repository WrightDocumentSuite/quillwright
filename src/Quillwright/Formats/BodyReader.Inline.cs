using System.Text;
using System.Xml;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Formats;

internal sealed partial class BodyReader
{
    private static readonly HashSet<string> ZeroWidthElements =
    [
        "proofErr", "permStart", "permEnd", "subDoc",
        "moveFromRangeStart", "moveFromRangeEnd", "moveToRangeStart", "moveToRangeEnd",
        "customXmlInsRangeStart", "customXmlInsRangeEnd", "customXmlDelRangeStart", "customXmlDelRangeEnd",
        "customXmlMoveFromRangeStart", "customXmlMoveFromRangeEnd",
        "customXmlMoveToRangeStart", "customXmlMoveToRangeEnd",
    ];

    private void ReadInline(XmlReader xml, string name, Paragraph paragraph)
    {
        switch (name)
        {
            case "r":
                ReadRun(xml, paragraph);
                return;
            case "hyperlink":
                ReadHyperlink(xml, paragraph);
                return;
            case "ins" or "del" or "moveFrom" or "moveTo":
                ReadRevision(xml, name, paragraph);
                return;
            case "sdt":
                ReadInlineControl(xml, paragraph);
                return;
            case "fldSimple":
                ReadSimpleField(xml, paragraph);
                return;
            case "smartTag" or "bdo" or "dir" or "customXml":
                ReadRawRange(xml, paragraph);
                return;
            case "bookmarkStart":
                paragraph.AddMark(new BookmarkStart
                {
                    Id = XmlHelp.AttrInt(xml, "id") ?? 0,
                    Name = XmlHelp.Attr(xml, "name") ?? string.Empty,
                    ColumnFirst = XmlHelp.AttrInt(xml, "colFirst"),
                    ColumnLast = XmlHelp.AttrInt(xml, "colLast"),
                });

                xml.Skip();
                return;
            case "bookmarkEnd":
                paragraph.AddMark(new BookmarkEnd { Id = XmlHelp.AttrInt(xml, "id") ?? 0 });
                xml.Skip();
                return;
            case "commentRangeStart":
                paragraph.AddMark(new CommentRangeStart { Id = XmlHelp.AttrInt(xml, "id") ?? 0 });
                xml.Skip();
                return;
            case "commentRangeEnd":
                paragraph.AddMark(new CommentRangeEnd { Id = XmlHelp.AttrInt(xml, "id") ?? 0 });
                xml.Skip();
                return;
            case "oMath" or "oMathPara" when DocxSchema.IsMathNamespace(xml.NamespaceURI):
            {
                string markup = xml.ReadOuterXml();
                InlineObject equation = OfficeMathReader.Parse(markup) as InlineObject ?? new RawInline(markup, isRunChild: false);
                paragraph.AppendRunObject(equation, RunFormat.Default, null);
                return;
            }

            default:
                if (ZeroWidthElements.Contains(name))
                    paragraph.AddMark(new RawMark(xml.ReadOuterXml()));
                else
                    paragraph.AppendRunObject(new RawInline(xml.ReadOuterXml(), isRunChild: false), RunFormat.Default, null);
                return;
        }
    }

    private void ReadRun(XmlReader xml, Paragraph paragraph)
    {
        string? attributes = XmlHelp.CaptureAttributes(xml);
        RunFormat format = RunFormat.Default;

        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "rPr":
                    format = _context.Intern(RunFormatReader.Read(reader));
                    return;
                case "t":
                    paragraph.AppendRunText(reader.ReadElementContentAsString(), format, RunKind.Text, attributes);
                    return;
                case "instrText":
                    paragraph.AppendRunText(reader.ReadElementContentAsString(), format, RunKind.FieldInstruction, attributes);
                    return;
                case "delText":
                    paragraph.AppendRunText(reader.ReadElementContentAsString(), format, RunKind.Deleted, attributes);
                    return;
                case "delInstrText":
                    paragraph.AppendRunText(reader.ReadElementContentAsString(), format, RunKind.DeletedFieldInstruction, attributes);
                    return;
                case "tab":
                    paragraph.AppendRunText("\t", format, RunKind.Text, attributes);
                    reader.Skip();
                    return;
                case "cr":
                    paragraph.AppendRunText("\n", format, RunKind.Text, attributes);
                    reader.Skip();
                    return;
                case "noBreakHyphen":
                    paragraph.AppendRunText("\u2011", format, RunKind.Text, attributes);
                    reader.Skip();
                    return;
                case "softHyphen":
                    paragraph.AppendRunText("\u00AD", format, RunKind.Text, attributes);
                    reader.Skip();
                    return;
                default:
                    ReadRunObject(reader, name, paragraph, format, attributes);
                    return;
            }
        });
    }

    private void ReadRunObject(XmlReader xml, string name, Paragraph paragraph, RunFormat format, string? attributes)
    {
        switch (name)
        {
            case "br":
            {
                BreakKind kind = OoxmlEnums.ParseBreakKind(XmlHelp.Attr(xml, "type"));
                BreakClear clear = OoxmlEnums.ParseBreakClear(XmlHelp.Attr(xml, "clear"));
                if (kind == BreakKind.Line && clear == BreakClear.None)
                    paragraph.AppendRunText("\n", format, RunKind.Text, attributes);
                else
                    paragraph.AppendRunObject(new Break { Kind = kind, Clear = clear }, format, attributes);
                xml.Skip();
                return;
            }

            case "sym":
                paragraph.AppendRunObject(
                    new SymbolCharacter
                    {
                        Font = XmlHelp.Attr(xml, "font") ?? string.Empty,
                        Character = int.TryParse(
                            XmlHelp.Attr(xml, "char"), System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out int code) ? code : 0,
                    },
                    format, attributes);
                xml.Skip();
                return;

            case "footnoteReference" or "endnoteReference":
                paragraph.AppendRunObject(
                    new NoteReference
                    {
                        IsEndnote = name[0] == 'e',
                        Id = XmlHelp.AttrInt(xml, "id") ?? 0,
                        CustomMark = XmlHelp.AttrBool(xml, "customMarkFollows") ?? false,
                    },
                    format, attributes);
                xml.Skip();
                return;

            case "footnoteRef" or "endnoteRef":
                paragraph.AppendRunObject(new NoteNumberMark { IsEndnote = name[0] == 'e' }, format, attributes);
                xml.Skip();
                return;

            case "commentReference":
                paragraph.AppendRunObject(new CommentReference { Id = XmlHelp.AttrInt(xml, "id") ?? 0 }, format, attributes);
                xml.Skip();
                return;

            case "fldChar":
                ReadFieldCharacter(xml, paragraph, format, attributes);
                return;

            case "drawing":
                ReadDrawing(xml, paragraph, format, attributes);
                return;

            case "lastRenderedPageBreak":
                paragraph.AppendRunObject(new RenderedPageBreak(), format, attributes);
                xml.Skip();
                return;

            case "separator" or "continuationSeparator":
                paragraph.AppendRunObject(new NoteSeparator { IsContinuation = name[0] == 'c' }, format, attributes);
                xml.Skip();
                return;

            case "ptab":
                paragraph.AppendRunObject(new PositionalTab(xml.ReadOuterXml()), format, attributes);
                return;

            case "AlternateContent" when xml.NamespaceURI == DocxSchema.NsMarkupCompatibility:
            {
                IDictionary<string, string> scope = XmlHelp.NamespacesInScope(xml);
                string markup = xml.ReadOuterXml();
                InlineObject resolved = MceReader.Read(markup, _context, scope);
                paragraph.AppendRunObject(resolved is RawInline ? ReadShape(markup, scope) : resolved, format, attributes);
                return;
            }

            case "pict":
            {
                IDictionary<string, string> scope = XmlHelp.NamespacesInScope(xml);
                paragraph.AppendRunObject(ReadShape(xml.ReadOuterXml(), scope), format, attributes);
                return;
            }

            case "object":
            {
                // The markup goes back exactly as it came; reading it only registers what the
                // package holds, so that a caller can find the object and pull it out.
                string markup = xml.ReadOuterXml();
                if (EmbeddedObjectReader.Read(markup, _context) is { } embedded)
                    _context.Document.EmbeddedObjectList.Add(embedded);
                paragraph.AppendRunObject(new RawInline(markup), format, attributes);
                return;
            }

            default:
                paragraph.AppendRunObject(new RawInline(xml.ReadOuterXml()), format, attributes);
                return;
        }
    }

    private static void ReadFieldCharacter(XmlReader xml, Paragraph paragraph, RunFormat format, string? attributes)
    {
        var field = new FieldCharacter
        {
            Kind = OoxmlEnums.ParseFieldCharKind(XmlHelp.Attr(xml, "fldCharType")),
            Locked = XmlHelp.AttrBool(xml, "fldLock") ?? false,
            Dirty = XmlHelp.AttrBool(xml, "dirty") ?? false,
        };

        if (xml.IsEmptyElement)
        {
            xml.Skip();
        }
        else
        {
            var buffer = new StringBuilder();
            XmlHelp.ForEachChild(xml, (reader, _) => buffer.Append(reader.ReadOuterXml()));
            if (buffer.Length > 0)
                field.FormFieldXml = buffer.ToString();
        }

        paragraph.AppendRunObject(field, format, attributes);
    }

    private void ReadDrawing(XmlReader xml, Paragraph paragraph, RunFormat format, string? attributes)
    {
        IDictionary<string, string> scope = XmlHelp.NamespacesInScope(xml);
        string markup = xml.ReadOuterXml();
        InlineObject anchored = DrawingReader.Parse(markup, _context) is { } picture ? picture : ReadShape(markup, scope);
        paragraph.AppendRunObject(anchored, format, attributes);
    }

    private void ReadHyperlink(XmlReader xml, Paragraph paragraph)
    {
        var link = new Hyperlink
        {
            RelationshipId = XmlHelp.RelAttr(xml),
            Anchor = XmlHelp.Attr(xml, "anchor"),
            Tooltip = XmlHelp.Attr(xml, "tooltip"),
            TargetFrame = XmlHelp.Attr(xml, "tgtFrame"),
            AddToHistory = XmlHelp.AttrBool(xml, "history") ?? true,
            Attributes = XmlHelp.CaptureAttributes(xml, "id", "anchor", "tooltip", "tgtFrame", "history"),
        };

        link.Url = _context.ExternalTarget(link.RelationshipId);
        if (link.RelationshipId is not null && link.Url is null && link.Anchor is null)
            _context.Warn(Diagnostics.WarningCode.MissingRelationship, $"Hyperlink refers to '{link.RelationshipId}', which has no relationship.");

        int start = paragraph.TextLength;
        XmlHelp.ForEachChild(xml, (reader, name) => ReadInline(reader, name, paragraph));
        paragraph.AddRange(link, start, paragraph.TextLength - start);
    }

    private void ReadRevision(XmlReader xml, string name, Paragraph paragraph)
    {
        var revision = new Revision
        {
            Kind = OoxmlEnums.ParseRevisionKind(name) ?? RevisionKind.Inserted,
            Id = XmlHelp.AttrInt(xml, "id") ?? 0,
            Author = XmlHelp.Attr(xml, "author"),
            Date = XmlHelp.ParseDate(XmlHelp.Attr(xml, "date")),
            MoveName = XmlHelp.Attr(xml, "name"),
            Attributes = XmlHelp.CaptureAttributes(xml, "id", "author", "date", "name"),
        };

        int start = paragraph.TextLength;
        XmlHelp.ForEachChild(xml, (reader, child) => ReadInline(reader, child, paragraph));
        paragraph.AddRange(revision, start, paragraph.TextLength - start);
    }

    private void ReadInlineControl(XmlReader xml, Paragraph paragraph)
    {
        var control = new InlineContentControl();
        int start = paragraph.TextLength;

        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "sdtPr":
                    control.PropertiesXml = reader.ReadOuterXml();
                    ReadControlProperties(control.PropertiesXml, out string? tag, out string? alias, out int? id);
                    (control.Tag, control.Alias, control.Id) = (tag, alias, id);
                    return;
                case "sdtEndPr":
                    control.EndPropertiesXml = reader.ReadOuterXml();
                    return;
                case "sdtContent":
                    XmlHelp.ForEachChild(reader, (content, child) => ReadInline(content, child, paragraph));
                    return;
                default:
                    reader.Skip();
                    return;
            }
        });

        paragraph.AddRange(control, start, paragraph.TextLength - start);
    }

    /// <summary>
    /// Reads a field written as one element (<c>w:fldSimple</c>). Its content is the cached
    /// result, so the range covers exactly what a caller replacing the result should replace.
    /// </summary>
    private void ReadSimpleField(XmlReader xml, Paragraph paragraph)
    {
        var field = new SimpleField
        {
            Instruction = (XmlHelp.Attr(xml, "instr") ?? string.Empty).Trim(),
            Dirty = XmlHelp.Attr(xml, "dirty") is { } dirty && dirty is not ("0" or "false" or "off"),
            Locked = XmlHelp.Attr(xml, "fldLock") is { } locked && locked is not ("0" or "false" or "off"),
            Attributes = XmlHelp.CaptureAttributes(xml, "instr", "dirty", "fldLock"),
        };

        int start = paragraph.TextLength;
        XmlHelp.ForEachChild(xml, (reader, child) =>
        {
            if (child == "fldData")
                field.DataXml = reader.ReadOuterXml();
            else
                ReadInline(reader, child, paragraph);
        });

        paragraph.AddRange(field, start, paragraph.TextLength - start);
    }

    private void ReadRawRange(XmlReader xml, Paragraph paragraph)
    {
        string qualifiedName = xml.Name;
        string prefix = BuildStartTag(xml);
        var properties = new StringBuilder();
        int start = paragraph.TextLength;

        XmlHelp.ForEachChild(xml, (reader, child) =>
        {
            if (child.EndsWith("Pr", StringComparison.Ordinal))
            {
                properties.Append(reader.ReadOuterXml());
                return;
            }

            ReadInline(reader, child, paragraph);
        });

        paragraph.AddRange(
            new RawRange(prefix + properties, $"</{qualifiedName}>"),
            start,
            paragraph.TextLength - start);
    }
}
