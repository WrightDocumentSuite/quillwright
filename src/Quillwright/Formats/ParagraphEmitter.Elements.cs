using System.Globalization;
using Quillwright.Model;
using Quillwright.Styles;
using Quillwright.Xml;

namespace Quillwright.Formats;

internal partial struct ParagraphEmitter
{
    private readonly void WriteObject(InlineObject? anchored, char placeholder)
    {
        switch (anchored)
        {
            case null:
                _writer.WriteRaw(placeholder switch
                {
                    '\t' => "<w:tab/>"u8,
                    '\n' => "<w:br/>"u8,
                    '\u00AD' => "<w:softHyphen/>"u8,
                    '\u2011' => "<w:noBreakHyphen/>"u8,
                    _ => ""u8,
                });

                return;
            case Break value:
                WriteBreak(value);
                return;
            case SymbolCharacter symbol:
                _writer.WriteRaw("<w:sym"u8);
                WordXml.Attribute(_writer, "w:font"u8, symbol.Font);
                WordXml.Attribute(_writer, "w:char"u8, symbol.Character.ToString("X4", CultureInfo.InvariantCulture));
                _writer.WriteRaw("/>"u8);
                return;
            case NoteReference note:
                _writer.WriteRaw(note.IsEndnote ? "<w:endnoteReference"u8 : "<w:footnoteReference"u8);
                if (note.CustomMark)
                    WordXml.Attribute(_writer, "w:customMarkFollows"u8, true);
                WordXml.Attribute(_writer, "w:id"u8, note.Id);
                _writer.WriteRaw("/>"u8);
                return;
            case NoteNumberMark number:
                _writer.WriteRaw(number.IsEndnote ? "<w:endnoteRef/>"u8 : "<w:footnoteRef/>"u8);
                return;
            case CommentReference comment:
                _writer.WriteRaw("<w:commentReference"u8);
                WordXml.Attribute(_writer, "w:id"u8, comment.Id);
                _writer.WriteRaw("/>"u8);
                return;
            case FieldCharacter field:
                WriteFieldCharacter(field);
                return;
            case Picture picture:
                DrawingWriter.Write(_writer, picture, _context.ResolvePicture(picture));
                return;
            case RenderedPageBreak:
                _writer.WriteRaw("<w:lastRenderedPageBreak/>"u8);
                return;
            case NoteSeparator separator:
                _writer.WriteRaw(separator.IsContinuation ? "<w:continuationSeparator/>"u8 : "<w:separator/>"u8);
                return;
            case PositionalTab tab:
                _writer.WriteRawXml(tab.Xml);
                return;
            case Shape shape:
                WriteShape(shape);
                return;
            case AlternateContent alternate:
                // The branches that were not selected come back as the bytes they arrived as,
                // so only the one this version modelled reflects an edit.
                _writer.WriteRawXml(alternate.Prefix);
                WriteObject(alternate.Content, alternate.Content.PlaceholderChar);
                _writer.WriteRawXml(alternate.Suffix);
                return;
            case MathObject equation:
                OfficeMathWriter.Write(_writer, equation);
                return;
            case ChartFrame chart:
                _writer.WriteRawXml(chart.Xml);
                return;
            case RawInline raw:
                _writer.WriteRawXml(raw.Xml);
                return;
        }
    }

    /// <summary>
    /// Writes the shape's own markup back untouched, with its content regenerated at each
    /// place the reader cut it out of.
    /// </summary>
    private readonly void WriteShape(Shape shape)
    {
        for (int i = 0; i < shape.Fragments.Count; i++)
        {
            _writer.WriteRawXml(shape.Fragments[i]);
            if (i < shape.Fragments.Count - 1)
                BodyWriter.WriteBlocks(_writer, shape.Content.Blocks, _context);
        }
    }

    private readonly void WriteBreak(Break value)
    {
        _writer.WriteRaw("<w:br"u8);
        if (value.Kind != BreakKind.Line)
            WordXml.Attribute(_writer, "w:type"u8, OoxmlEnums.Name(value.Kind));
        if (value.Clear != BreakClear.None)
            WordXml.Attribute(_writer, "w:clear"u8, OoxmlEnums.Name(value.Clear));
        _writer.WriteRaw("/>"u8);
    }

    private readonly void WriteFieldCharacter(FieldCharacter field)
    {
        _writer.WriteRaw("<w:fldChar"u8);
        WordXml.Attribute(_writer, "w:fldCharType"u8, OoxmlEnums.Name(field.Kind));
        if (field.Locked)
            WordXml.Attribute(_writer, "w:fldLock"u8, true);
        if (field.Dirty)
            WordXml.Attribute(_writer, "w:dirty"u8, true);

        if (field.FormFieldXml is { } form)
        {
            _writer.WriteRaw(">"u8);
            _writer.WriteRawXml(form);
            _writer.WriteRaw("</w:fldChar>"u8);
            return;
        }

        _writer.WriteRaw("/>"u8);
    }

    private readonly void WriteMark(InlineMark mark)
    {
        switch (mark)
        {
            case BookmarkStart start:
                _writer.WriteRaw("<w:bookmarkStart"u8);
                WordXml.Attribute(_writer, "w:id"u8, start.Id);
                WordXml.Attribute(_writer, "w:name"u8, start.Name);
                WordXml.Attribute(_writer, "w:colFirst"u8, start.ColumnFirst);
                WordXml.Attribute(_writer, "w:colLast"u8, start.ColumnLast);
                _writer.WriteRaw("/>"u8);
                return;
            case BookmarkEnd end:
                _writer.WriteRaw("<w:bookmarkEnd"u8);
                WordXml.Attribute(_writer, "w:id"u8, end.Id);
                _writer.WriteRaw("/>"u8);
                return;
            case CommentRangeStart commentStart:
                _writer.WriteRaw("<w:commentRangeStart"u8);
                WordXml.Attribute(_writer, "w:id"u8, commentStart.Id);
                _writer.WriteRaw("/>"u8);
                return;
            case CommentRangeEnd commentEnd:
                _writer.WriteRaw("<w:commentRangeEnd"u8);
                WordXml.Attribute(_writer, "w:id"u8, commentEnd.Id);
                _writer.WriteRaw("/>"u8);
                return;
            case RawMark raw:
                _writer.WriteRawXml(raw.Xml);
                return;
        }
    }

    private readonly void WriteRangePrefix(InlineRange range)
    {
        switch (range)
        {
            case Hyperlink link:
                _writer.WriteRaw("<w:hyperlink"u8);
                WordXml.Attribute(_writer, "r:id"u8, _context.ResolveHyperlink(link));
                WordXml.Attribute(_writer, "w:anchor"u8, link.Anchor);
                WordXml.Attribute(_writer, "w:tooltip"u8, link.Tooltip);
                WordXml.Attribute(_writer, "w:tgtFrame"u8, link.TargetFrame);
                if (!link.AddToHistory)
                    WordXml.Attribute(_writer, "w:history"u8, false);
                if (link.Attributes is { } attributes)
                    _writer.WriteRawXml(attributes);
                _writer.WriteRaw(">"u8);
                return;
            case Revision revision:
                WordXml.Open(_writer, RevisionName(revision.Kind));
                WordXml.Attribute(_writer, "w:id"u8, revision.Id);
                WordXml.Attribute(_writer, "w:author"u8, revision.Author);
                if (revision.Date is { } date)
                    WordXml.Attribute(_writer, "w:date"u8, date.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                WordXml.Attribute(_writer, "w:name"u8, revision.MoveName);
                if (revision.Attributes is { } revisionAttributes)
                    _writer.WriteRawXml(revisionAttributes);
                _writer.WriteRaw(">"u8);
                return;
            case SimpleField field:
                _writer.WriteRaw("<w:fldSimple"u8);
                WordXml.Attribute(_writer, "w:instr"u8, field.Instruction);
                if (field.Locked)
                    WordXml.Attribute(_writer, "w:fldLock"u8, true);
                if (field.Dirty)
                    WordXml.Attribute(_writer, "w:dirty"u8, true);
                if (field.Attributes is { } fieldAttributes)
                    _writer.WriteRawXml(fieldAttributes);
                _writer.WriteRaw(">"u8);
                RawXml.Write(_writer, field.DataXml);
                return;
            case InlineContentControl control:
                _writer.WriteRaw("<w:sdt>"u8);
                WriteControlProperties(control);
                _writer.WriteRaw("<w:sdtContent>"u8);
                return;
            case RawRange raw:
                _writer.WriteRawXml(raw.Prefix);
                return;
        }
    }

    private readonly void WriteControlProperties(InlineContentControl control)
    {
        if (control.PropertiesXml is { } properties)
        {
            _writer.WriteRawXml(properties);
        }
        else
        {
            _writer.WriteRaw("<w:sdtPr>"u8);
            WordXml.Value(_writer, "alias"u8, control.Alias);
            WordXml.Value(_writer, "tag"u8, control.Tag);
            WordXml.Value(_writer, "id"u8, control.Id);
            _writer.WriteRaw("</w:sdtPr>"u8);
        }

        RawXml.Write(_writer, control.EndPropertiesXml);
    }

    private readonly void CloseRange(AnchoredRange range)
    {
        switch (range.Range)
        {
            case Hyperlink:
                _writer.WriteRaw("</w:hyperlink>"u8);
                return;
            case Revision revision:
                WordXml.Close(_writer, RevisionName(revision.Kind));
                return;
            case SimpleField:
                _writer.WriteRaw("</w:fldSimple>"u8);
                return;
            case InlineContentControl:
                _writer.WriteRaw("</w:sdtContent></w:sdt>"u8);
                return;
            case RawRange raw:
                _writer.WriteRawXml(raw.Suffix);
                return;
        }
    }

    private static ReadOnlySpan<byte> RevisionName(RevisionKind kind) => kind switch
    {
        RevisionKind.Deleted => "del"u8,
        RevisionKind.MovedFrom => "moveFrom"u8,
        RevisionKind.MovedTo => "moveTo"u8,
        _ => "ins"u8,
    };
}
