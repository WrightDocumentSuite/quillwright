using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Doc;

/// <summary>Turns the section modifiers of a legacy document into a page setup.</summary>
internal static partial class SprmTranslator
{
    /// <summary>Applies the section modifiers of a property list to a page setup.</summary>
    /// <param name="properties">The page setup to update in place.</param>
    /// <param name="modifiers">The packed modifiers.</param>
    public static void ApplySection(SectionProperties properties, ReadOnlySpan<byte> modifiers)
    {
        var reader = new SprmReader(modifiers);
        while (reader.TryRead(out Sprm sprm))
        {
            switch (sprm.Opcode)
            {
                case SprmCode.SectionBreak:
                    properties.Start = TranslateSectionStart(sprm.Byte);
                    break;
                case SprmCode.Orientation:
                    properties.Orientation = sprm.Byte == 2 ? PageOrientation.Landscape : PageOrientation.Portrait;
                    break;
                case SprmCode.PageWidth:
                    properties.PageWidth = Length.FromTwips(sprm.UInt16);
                    break;
                case SprmCode.PageHeight:
                    properties.PageHeight = Length.FromTwips(sprm.UInt16);
                    break;
                case SprmCode.MarginLeft:
                    properties.Margins.Left = Length.FromTwips(sprm.UInt16);
                    break;
                case SprmCode.MarginRight:
                    properties.Margins.Right = Length.FromTwips(sprm.UInt16);
                    break;
                case SprmCode.MarginTop:
                    properties.Margins.Top = Length.FromTwips(sprm.Int16);
                    break;
                case SprmCode.MarginBottom:
                    properties.Margins.Bottom = Length.FromTwips(sprm.Int16);
                    break;
                case SprmCode.MarginHeader:
                    properties.Margins.Header = Length.FromTwips(sprm.UInt16);
                    break;
                case SprmCode.MarginFooter:
                    properties.Margins.Footer = Length.FromTwips(sprm.UInt16);
                    break;
                case SprmCode.Gutter:
                    properties.Margins.Gutter = Length.FromTwips(sprm.UInt16);
                    break;
                case SprmCode.TitlePage:
                    properties.DifferentFirstPage = sprm.Byte != 0;
                    break;
                case SprmCode.ColumnCount:
                    properties.Columns.Count = sprm.UInt16 + 1;
                    break;
                case SprmCode.ColumnSpacing:
                    properties.Columns.Space = Length.FromTwips(sprm.Int16);
                    break;
                case SprmCode.PageNumberStart:
                    properties.PageNumbering.Start = sprm.UInt16;
                    break;
                case SprmCode.PageNumberFormat:
                    properties.PageNumbering.Format = DocNumberFormat.Of(sprm.Byte);
                    break;
            }
        }
    }

    private static SectionStart TranslateSectionStart(byte value) => value switch
    {
        0 => SectionStart.Continuous,
        1 => SectionStart.NextColumn,
        3 => SectionStart.EvenPage,
        4 => SectionStart.OddPage,
        _ => SectionStart.NextPage,
    };
}
