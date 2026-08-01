using System.Globalization;
using System.Xml;
using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Formats;

/// <summary>
/// Reads the colour scheme out of the theme part, and the map that says which of its slots a
/// WordprocessingML colour means (ECMA-376 part 1 §20.1.6.2, ISO/IEC 29500-1 §17.15.1.20).
/// </summary>
/// <remarks>
/// The theme part is carried through untouched, because almost all of it — fonts, fills, line
/// styles, effects — is drawing-layer material the model has no place for. Its twelve colours
/// are the exception: without them a run that names a theme slot has a name and no value, so
/// the scheme alone is read out and the part itself is still written back as it arrived.
/// </remarks>
internal static class ThemeReader
{
    /// <summary>Reads the theme, or returns <see langword="null"/> when the package has none.</summary>
    /// <param name="xml">The bytes of the theme part.</param>
    /// <param name="mapping">The <c>w:clrSchemeMapping</c> element from the settings part, if any.</param>
    public static DocumentTheme? Read(byte[]? xml, string? mapping)
    {
        if (xml is null || xml.Length == 0)
            return null;

        var theme = new DocumentTheme();
        try
        {
            using XmlReader reader = XmlReader.Create(new MemoryStream(xml), Xml.XmlDefaults.ReaderSettings);
            ReadScheme(reader, theme);
        }
        catch (XmlException)
        {
            return null;
        }

        if (theme.Scheme.Count == 0)
            return null;

        ReadMapping(mapping, theme);
        return theme;
    }

    /// <summary>
    /// Walks to the colour scheme and reads each of its slots. A slot holds one colour
    /// element, which is usually a literal but may name a system colour — and a system colour
    /// carries the value it last resolved to, which is the only value a file can offer.
    /// </summary>
    private static void ReadScheme(XmlReader reader, DocumentTheme theme)
    {
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;

            if (reader.LocalName == "theme" && theme.Name is null)
            {
                theme.Name = reader.GetAttribute("name");
                continue;
            }

            if (reader.LocalName != "clrScheme")
                continue;

            using XmlReader scheme = reader.ReadSubtree();
            scheme.Read();
            ReadSlots(scheme, theme);
            return;
        }
    }

    private static void ReadSlots(XmlReader scheme, DocumentTheme theme)
    {
        ThemeColorSlot slot = ThemeColorSlot.None;
        while (scheme.Read())
        {
            if (scheme.NodeType != XmlNodeType.Element)
                continue;

            if (Slot(scheme.LocalName) is { } named)
            {
                slot = named;
                continue;
            }

            if (slot == ThemeColorSlot.None)
                continue;

            if (Value(scheme) is { } value)
                theme.Define(slot, value);

            slot = ThemeColorSlot.None;
        }
    }

    /// <summary>The colour one slot holds, whichever of the two ways it says it.</summary>
    private static uint? Value(XmlReader scheme) => scheme.LocalName switch
    {
        "srgbClr" => Hex(scheme.GetAttribute("val")),
        "sysClr" => Hex(scheme.GetAttribute("lastClr")),
        _ => null,
    };

    private static uint? Hex(string? value) =>
        value is { Length: 6 } &&
        uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsed)
            ? parsed
            : null;

    /// <summary>
    /// The map from the names a run uses to the slots the theme defines. Only the four that
    /// can be swapped are mapped; the rest name the theme's slots directly.
    /// </summary>
    private static void ReadMapping(string? mapping, DocumentTheme theme)
    {
        if (mapping is null)
            return;

        try
        {
            string scoped =
                $"<qw:s xmlns:qw=\"urn:quillwright\" xmlns:w=\"{DocxSchema.NsWord}\">{mapping}</qw:s>";
            using XmlReader reader = XmlReader.Create(new StringReader(scoped), Xml.XmlDefaults.ReaderSettings);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "clrSchemeMapping")
                {
                    Map(reader, theme, "bg1", ThemeColorSlot.Background1);
                    Map(reader, theme, "t1", ThemeColorSlot.Text1);
                    Map(reader, theme, "bg2", ThemeColorSlot.Background2);
                    Map(reader, theme, "t2", ThemeColorSlot.Text2);
                    return;
                }
            }
        }
        catch (XmlException)
        {
            // A mapping that cannot be read leaves the defaults, which is what Word assumes.
        }
    }

    private static void Map(XmlReader reader, DocumentTheme theme, string attribute, ThemeColorSlot from)
    {
        string? target = XmlHelp.Attr(reader, attribute);
        theme.Map(from, target is null ? Default(from) : WordColor.ParseThemeSlot(target));
    }

    /// <summary>Which theme slot a name means when the settings say nothing (§17.15.1.20).</summary>
    private static ThemeColorSlot Default(ThemeColorSlot slot) => slot switch
    {
        ThemeColorSlot.Background1 => ThemeColorSlot.Light1,
        ThemeColorSlot.Text1 => ThemeColorSlot.Dark1,
        ThemeColorSlot.Background2 => ThemeColorSlot.Light2,
        _ => ThemeColorSlot.Dark2,
    };

    /// <summary>The slot one child of the scheme stands for.</summary>
    private static ThemeColorSlot? Slot(string name) => name switch
    {
        "dk1" => ThemeColorSlot.Dark1,
        "lt1" => ThemeColorSlot.Light1,
        "dk2" => ThemeColorSlot.Dark2,
        "lt2" => ThemeColorSlot.Light2,
        "accent1" => ThemeColorSlot.Accent1,
        "accent2" => ThemeColorSlot.Accent2,
        "accent3" => ThemeColorSlot.Accent3,
        "accent4" => ThemeColorSlot.Accent4,
        "accent5" => ThemeColorSlot.Accent5,
        "accent6" => ThemeColorSlot.Accent6,
        "hlink" => ThemeColorSlot.Hyperlink,
        "folHlink" => ThemeColorSlot.FollowedHyperlink,
        _ => null,
    };
}
