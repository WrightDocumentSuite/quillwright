using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>List markers.</summary>
internal sealed partial class PageComposer
{
    private readonly NumberingCounter _numbering;

    /// <summary>
    /// The marker a list paragraph opens with, as content for the line breaker to place: the
    /// bullet or number, then whatever the level says separates it from the text.
    /// </summary>
    /// <remarks>
    /// The marker is content on the first line rather than something drawn beside the paragraph,
    /// which is what makes a hanging indent work: the marker sits in the outdent, the tab after it
    /// jumps to the left indent, and the text follows from there.
    /// </remarks>
    private IReadOnlyList<InlineItem>? NumberPrefix(Paragraph paragraph, ParagraphFormat format)
    {
        NumberLabel? next = _rehearsing ? _numbering.Peek(format) : _numbering.Next(format);
        if (next is not { } label)
            return null;

        RunFormat resolved = _context.Resolver.ResolveNumberingSymbolFormat(paragraph)
            ?? _context.Resolver.ResolveMarkFormat(paragraph);

        CharacterStyle style = _measurer.Style(resolved);
        List<InlineItem> prefix = [];

        if (label.Text.Length > 0)
            prefix.Add(InlineItem.OfText(label.Text, style, null));

        switch (label.Level.Suffix)
        {
            case ListLevelSuffix.Space:
                prefix.Add(InlineItem.OfText(" ", style, null));
                break;

            case ListLevelSuffix.Nothing:
                break;

            default:
                prefix.Add(InlineItem.Control(InlineKind.Tab, style));
                break;
        }

        return prefix.Count == 0 ? null : prefix;
    }
}
