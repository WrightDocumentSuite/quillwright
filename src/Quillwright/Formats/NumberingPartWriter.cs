using Quillwright.Model;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>Writes the numbering part (<c>numbering.xml</c>).</summary>
internal static class NumberingPartWriter
{
    /// <summary>Writes the whole part.</summary>
    public static void Write(Utf8XmlWriter writer, NumberingDefinitions numbering)
    {
        WordXml.OpenRoot(writer, "numbering"u8, numbering.Attributes);

        foreach (string bullet in numbering.PictureBullets)
            writer.WriteRawXml(bullet);

        foreach (AbstractNumbering definition in numbering.Definitions)
            WriteDefinition(writer, definition);

        foreach (NumberingInstance instance in numbering.Instances)
        {
            writer.WriteRaw("<w:num"u8);
            WordXml.Attribute(writer, "w:numId"u8, instance.Id);
            writer.WriteRaw("><w:abstractNumId"u8);
            WordXml.Attribute(writer, "w:val"u8, instance.AbstractId);
            writer.WriteRaw("/>"u8);

            foreach (NumberingLevelOverride levelOverride in instance.Overrides)
            {
                writer.WriteRaw("<w:lvlOverride"u8);
                WordXml.Attribute(writer, "w:ilvl"u8, levelOverride.Level);
                writer.WriteRaw(">"u8);
                if (levelOverride.StartOverride is { } start)
                {
                    writer.WriteRaw("<w:startOverride"u8);
                    WordXml.Attribute(writer, "w:val"u8, start);
                    writer.WriteRaw("/>"u8);
                }

                if (levelOverride.Definition is { } definition)
                    WriteLevel(writer, definition);
                writer.WriteRaw("</w:lvlOverride>"u8);
            }

            writer.WriteRaw("</w:num>"u8);
        }

        RawXml.Write(writer, numbering.CleanupXml);
        writer.WriteRaw("</w:numbering>"u8);
    }

    private static void WriteDefinition(Utf8XmlWriter writer, AbstractNumbering definition)
    {
        writer.WriteRaw("<w:abstractNum"u8);
        WordXml.Attribute(writer, "w:abstractNumId"u8, definition.Id);
        if (definition.Attributes is { } attributes)
            writer.WriteRawXml(attributes);
        writer.WriteRaw(">"u8);

        RawXml.Write(writer, definition.NsidXml);
        WordXml.Value(writer, "multiLevelType"u8, definition.MultiLevelType);
        RawXml.Write(writer, definition.TemplateXml);
        RawXml.Write(writer, definition.NameXml);
        WordXml.Value(writer, "styleLink"u8, definition.StyleLink);
        WordXml.Value(writer, "numStyleLink"u8, definition.NumberingStyleLink);

        foreach (NumberingLevel level in definition.Levels)
            WriteLevel(writer, level);

        writer.WriteRaw("</w:abstractNum>"u8);
    }

    private static void WriteLevel(Utf8XmlWriter writer, NumberingLevel level)
    {
        writer.WriteRaw("<w:lvl"u8);
        WordXml.Attribute(writer, "w:ilvl"u8, level.Level);
        if (level.Attributes is { } attributes)
            writer.WriteRawXml(attributes);
        writer.WriteRaw(">"u8);

        WordXml.Value(writer, "start"u8, level.Start);
        WordXml.Value(writer, "numFmt"u8, OoxmlEnums.Name(level.Format, level.CustomFormat));
        WordXml.Value(writer, "lvlRestart"u8, level.RestartAfter);
        WordXml.Value(writer, "pStyle"u8, level.StyleId);
        if (level.IsLegal)
            writer.WriteRaw("<w:isLgl/>"u8);
        if (level.Suffix != Styles.ListLevelSuffix.Tab)
            WordXml.Value(writer, "suff"u8, OoxmlEnums.Name(level.Suffix));
        WordXml.Value(writer, "lvlText"u8, level.Text);
        WordXml.Value(writer, "lvlPicBulletId"u8, level.PictureBulletId);
        RawXml.Write(writer, level.LegacyXml);
        WordXml.Value(writer, "lvlJc"u8, OoxmlEnums.Name(level.Alignment, writer.Strict));

        if (!level.ParagraphFormat.IsEmpty)
        {
            writer.WriteRaw("<w:pPr>"u8);
            ParagraphFormatWriter.WriteBody(writer, level.ParagraphFormat);
            writer.WriteRaw("</w:pPr>"u8);
        }

        RunFormatWriter.Write(writer, level.RunFormat);
        RawXml.Write(writer, level.Extensions);
        writer.WriteRaw("</w:lvl>"u8);
    }
}
