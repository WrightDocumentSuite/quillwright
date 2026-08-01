using System.Xml;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Formats;

/// <summary>
/// Reads a <c>w:footnotePr</c> or <c>w:endnotePr</c> element, wherever it was kept.
/// </summary>
/// <remarks>
/// The element itself is preserved as the bytes it arrived as, so this never has to be complete:
/// what it does not recognise is still written back. It recognises the four things that decide
/// what a reader sees — where the notes go, how they are numbered, from what, and when the count
/// starts again.
/// </remarks>
internal static class NotePropertiesReader
{
    /// <summary>Reads the element, or answers the defaults when there is none.</summary>
    /// <param name="xml">The preserved element, or <see langword="null"/>.</param>
    /// <param name="endnotes">Whether these are endnotes, which default differently.</param>
    public static NoteProperties Parse(string? xml, bool endnotes)
    {
        NoteProperties defaults = endnotes ? NoteProperties.EndnoteDefaults : NoteProperties.FootnoteDefaults;
        if (string.IsNullOrWhiteSpace(xml))
            return defaults;

        NotePosition position = defaults.Position;
        ListNumberFormat format = defaults.NumberFormat;
        int start = defaults.Start;
        NoteRestart restart = defaults.Restart;

        try
        {
            // The element was captured out of a document and may lean on a prefix its ancestors
            // declared, so it is read under a root that declares the one it needs.
            using var reader = XmlReader.Create(
                new StringReader($"<q:root xmlns:q=\"{DocxSchema.NsWord}\" xmlns:w=\"{DocxSchema.NsWord}\">{xml}</q:root>"),
                Xml.XmlDefaults.ReaderSettings);

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                switch (reader.LocalName)
                {
                    case "pos":
                        position = ParsePosition(XmlHelp.Val(reader), position);
                        break;
                    case "numFmt":
                        format = OoxmlEnums.ParseNumberFormat(XmlHelp.Val(reader)).Format;
                        break;
                    case "numStart":
                        start = XmlHelp.ValInt(reader) ?? start;
                        break;
                    case "numRestart":
                        restart = ParseRestart(XmlHelp.Val(reader), restart);
                        break;
                    default:
                        break;
                }
            }
        }
        catch (XmlException)
        {
            // Markup this cannot read is still written back; the defaults describe it well enough.
            return defaults;
        }

        return new NoteProperties
        {
            Position = position,
            NumberFormat = format,
            Start = Math.Max(1, start),
            Restart = restart,
        };
    }

    private static NotePosition ParsePosition(string? value, NotePosition fallback) => value switch
    {
        "pageBottom" => NotePosition.PageBottom,
        "beneathText" => NotePosition.BeneathText,
        "sectEnd" => NotePosition.SectionEnd,
        "docEnd" => NotePosition.DocumentEnd,
        _ => fallback,
    };

    private static NoteRestart ParseRestart(string? value, NoteRestart fallback) => value switch
    {
        "continuous" => NoteRestart.Continuous,
        "eachSect" => NoteRestart.EachSection,
        "eachPage" => NoteRestart.EachPage,
        _ => fallback,
    };
}
