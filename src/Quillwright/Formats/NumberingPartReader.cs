using System.Xml;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Formats;

/// <summary>Reads the numbering part (<c>numbering.xml</c>).</summary>
internal static class NumberingPartReader
{
    /// <summary>Reads the whole part into the document's numbering definitions.</summary>
    public static void Read(XmlReader xml, NumberingDefinitions numbering, LoadContext context)
    {
        StylesPartReader.MoveToRoot(xml, "numbering");
        numbering.Attributes = XmlHelp.CaptureRootAttributes(xml);

        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "numPicBullet":
                    numbering.PictureBullets.Add(reader.ReadOuterXml());
                    return;
                case "abstractNum":
                    numbering.Definitions.Add(ReadDefinition(reader, context));
                    return;
                case "num":
                    numbering.Instances.Add(ReadInstance(reader, context));
                    return;
                case "numIdMacAtCleanup":
                    numbering.CleanupXml = reader.ReadOuterXml();
                    return;
                default:
                    reader.Skip();
                    return;
            }
        });
    }

    private static AbstractNumbering ReadDefinition(XmlReader xml, LoadContext context)
    {
        var definition = new AbstractNumbering
        {
            Id = XmlHelp.AttrInt(xml, "abstractNumId") ?? 0,
            Attributes = XmlHelp.CaptureAttributes(xml, "abstractNumId"),
        };

        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "multiLevelType": definition.MultiLevelType = XmlHelp.Val(reader); reader.Skip(); return;
                case "numStyleLink": definition.NumberingStyleLink = XmlHelp.Val(reader); reader.Skip(); return;
                case "styleLink": definition.StyleLink = XmlHelp.Val(reader); reader.Skip(); return;
                case "lvl": definition.Levels.Add(ReadLevel(reader, context)); return;
                case "nsid": definition.NsidXml = reader.ReadOuterXml(); return;
                case "tmpl": definition.TemplateXml = reader.ReadOuterXml(); return;
                case "name": definition.NameXml = reader.ReadOuterXml(); return;
                default: reader.Skip(); return;
            }
        });

        return definition;
    }

    private static NumberingInstance ReadInstance(XmlReader xml, LoadContext context)
    {
        var instance = new NumberingInstance { Id = XmlHelp.AttrInt(xml, "numId") ?? 0 };
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "abstractNumId":
                    instance.AbstractId = XmlHelp.ValInt(reader) ?? 0;
                    reader.Skip();
                    return;
                case "lvlOverride":
                    instance.Overrides.Add(ReadOverride(reader, context));
                    return;
                default:
                    reader.Skip();
                    return;
            }
        });

        return instance;
    }

    private static NumberingLevelOverride ReadOverride(XmlReader xml, LoadContext context)
    {
        var result = new NumberingLevelOverride { Level = XmlHelp.AttrInt(xml, "ilvl") ?? 0 };
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "startOverride":
                    result.StartOverride = XmlHelp.ValInt(reader);
                    reader.Skip();
                    return;
                case "lvl":
                    result.Definition = ReadLevel(reader, context);
                    return;
                default:
                    reader.Skip();
                    return;
            }
        });

        return result;
    }

    private static NumberingLevel ReadLevel(XmlReader xml, LoadContext context)
    {
        var level = new NumberingLevel
        {
            Level = XmlHelp.AttrInt(xml, "ilvl") ?? 0,
            Attributes = XmlHelp.CaptureAttributes(xml, "ilvl"),
        };

        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "start": level.Start = XmlHelp.ValInt(reader) ?? 1; reader.Skip(); return;
                case "numFmt":
                    (level.Format, level.CustomFormat) = OoxmlEnums.ParseNumberFormat(XmlHelp.Val(reader));
                    reader.Skip();
                    return;
                case "lvlRestart": level.RestartAfter = XmlHelp.ValInt(reader); reader.Skip(); return;
                case "pStyle": level.StyleId = XmlHelp.Val(reader); reader.Skip(); return;
                case "isLgl": level.IsLegal = XmlHelp.Toggle(reader); reader.Skip(); return;
                case "suff": level.Suffix = OoxmlEnums.ParseLevelSuffix(XmlHelp.Val(reader)); reader.Skip(); return;
                case "lvlText": level.Text = XmlHelp.Val(reader) ?? string.Empty; reader.Skip(); return;
                case "lvlPicBulletId": level.PictureBulletId = XmlHelp.ValInt(reader); reader.Skip(); return;
                case "legacy": level.LegacyXml = reader.ReadOuterXml(); return;
                case "lvlJc": level.Alignment = OoxmlEnums.ParseAlignment(XmlHelp.Val(reader)) ?? ParagraphAlignment.Left; reader.Skip(); return;
                case "pPr": level.ParagraphFormat = context.Intern(ParagraphFormatReader.Read(reader).Format); return;
                case "rPr": level.RunFormat = context.Intern(RunFormatReader.Read(reader)); return;
                default: level.Extensions = (level.Extensions ?? string.Empty) + reader.ReadOuterXml(); return;
            }
        });

        return level;
    }
}
