using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Model;

/// <summary>One level of a list definition (<c>w:lvl</c>).</summary>
public sealed class NumberingLevel
{
    /// <summary>Zero-based depth of the level.</summary>
    public int Level { get; set; }

    /// <summary>Number the level starts counting from (<c>w:start</c>).</summary>
    public int Start { get; set; } = 1;

    /// <summary>Numbering scheme (<c>w:numFmt</c>).</summary>
    public ListNumberFormat Format { get; set; } = ListNumberFormat.Decimal;

    /// <summary>The OOXML name of a scheme this version does not know.</summary>
    public string? CustomFormat { get; set; }

    /// <summary>Pattern of the label, with <c>%1</c>…<c>%9</c> standing for level counters (<c>w:lvlText</c>).</summary>
    public string Text { get; set; } = "%1.";

    /// <summary>Alignment of the label (<c>w:lvlJc</c>).</summary>
    public ParagraphAlignment Alignment { get; set; } = ParagraphAlignment.Left;

    /// <summary>What separates the label from the text (<c>w:suff</c>).</summary>
    public ListLevelSuffix Suffix { get; set; } = ListLevelSuffix.Tab;

    /// <summary>Deepest level whose change restarts this counter (<c>w:lvlRestart</c>).</summary>
    public int? RestartAfter { get; set; }

    /// <summary>Displays the level using legal numbering (<c>w:isLgl</c>).</summary>
    public bool IsLegal { get; set; }

    /// <summary>Paragraph style the level is bound to (<c>w:pStyle</c>).</summary>
    public string? StyleId { get; set; }

    /// <summary>Identifier of the picture used as a bullet (<c>w:lvlPicBulletId</c>).</summary>
    public int? PictureBulletId { get; set; }

    /// <summary>The Word 6 compatibility element, kept verbatim (<c>w:legacy</c>).</summary>
    public string? LegacyXml { get; set; }

    /// <summary>Paragraph formatting the level contributes.</summary>
    public ParagraphFormat ParagraphFormat { get; set; } = ParagraphFormat.Default;

    /// <summary>Character formatting of the label.</summary>
    public RunFormat RunFormat { get; set; } = RunFormat.Default;

    /// <summary>Attributes of <c>w:lvl</c> this version does not model, kept verbatim.</summary>
    public string? Attributes { get; set; }

    /// <summary>Children of <c>w:lvl</c> this version does not model, kept verbatim.</summary>
    public string? Extensions { get; set; }

    /// <summary>Returns an independent copy.</summary>
    public NumberingLevel Clone() => (NumberingLevel)MemberwiseClone();
}

/// <summary>
/// A list definition shared by any number of list instances (<c>w:abstractNum</c>).
/// </summary>
public sealed class AbstractNumbering
{
    /// <summary>Identifier referenced by numbering instances.</summary>
    public int Id { get; set; }

    /// <summary>Whether the definition is single-level, multi-level or hybrid (<c>w:multiLevelType</c>).</summary>
    public string? MultiLevelType { get; set; }

    /// <summary>Numbering style that supplies this definition (<c>w:numStyleLink</c>).</summary>
    public string? NumberingStyleLink { get; set; }

    /// <summary>Numbering style this definition belongs to (<c>w:styleLink</c>).</summary>
    public string? StyleLink { get; set; }

    /// <summary>The levels of the list, deepest last.</summary>
    public List<NumberingLevel> Levels { get; } = [];

    /// <summary>The <c>w:nsid</c> element Word uses to match definitions, kept verbatim.</summary>
    public string? NsidXml { get; set; }

    /// <summary>The <c>w:tmpl</c> element identifying the gallery template, kept verbatim.</summary>
    public string? TemplateXml { get; set; }

    /// <summary>The <c>w:name</c> element, kept verbatim.</summary>
    public string? NameXml { get; set; }

    /// <summary>Attributes of <c>w:abstractNum</c> this version does not model, kept verbatim.</summary>
    public string? Attributes { get; set; }
}

/// <summary>A level override on a list instance (<c>w:lvlOverride</c>).</summary>
public sealed class NumberingLevelOverride
{
    /// <summary>Zero-based depth of the overridden level.</summary>
    public int Level { get; set; }

    /// <summary>Number the level restarts at (<c>w:startOverride</c>).</summary>
    public int? StartOverride { get; set; }

    /// <summary>A complete replacement definition of the level, or <see langword="null"/>.</summary>
    public NumberingLevel? Definition { get; set; }
}

/// <summary>
/// A list as it appears in the document (<c>w:num</c>): a reference to a definition plus
/// any per-instance overrides. Paragraphs point at the instance, not the definition, which
/// is what lets two lists share formatting but count separately.
/// </summary>
public sealed class NumberingInstance
{
    /// <summary>Identifier referenced by <c>w:numPr/w:numId</c>.</summary>
    public int Id { get; set; }

    /// <summary>Identifier of the definition this instance uses.</summary>
    public int AbstractId { get; set; }

    /// <summary>Per-instance level overrides.</summary>
    public List<NumberingLevelOverride> Overrides { get; } = [];
}

/// <summary>
/// The numbering part of a document (<c>numbering.xml</c>): the list definitions, the list
/// instances and the picture bullets they use.
/// </summary>
public sealed class NumberingDefinitions
{
    /// <summary>The list definitions.</summary>
    public List<AbstractNumbering> Definitions { get; } = [];

    /// <summary>The list instances.</summary>
    public List<NumberingInstance> Instances { get; } = [];

    /// <summary>The picture-bullet declarations, kept verbatim (<c>w:numPicBullet</c>).</summary>
    public List<string> PictureBullets { get; } = [];

    /// <summary>Attributes of <c>w:numbering</c> this version does not model, kept verbatim.</summary>
    public string? Attributes { get; set; }

    /// <summary>
    /// The identifier Word remembers for its own renumbering (<c>w:numIdMacAtCleanup</c>),
    /// kept verbatim. The schema puts it after everything else in the part.
    /// </summary>
    public string? CleanupXml { get; set; }

    /// <summary>
    /// The document these definitions belong to, which is what makes a <c>w:numStyleLink</c>
    /// resolvable: the link names a style, and the style lives outside this part.
    /// </summary>
    internal WordDocument? Owner { get; set; }

    /// <summary>Returns the definition an instance points at, following it through the instance list.</summary>
    /// <param name="numberingId">Identifier of the list instance.</param>
    /// <remarks>
    /// A definition that only refers to a numbering style (§17.9.21) is followed through to
    /// the one that actually declares the levels.
    /// </remarks>
    public AbstractNumbering? ResolveDefinition(int numberingId) => ResolveDefinition(numberingId, depth: 0);

    /// <summary>Returns the level of a list instance, applying any override.</summary>
    /// <param name="numberingId">Identifier of the list instance.</param>
    /// <param name="level">Zero-based depth.</param>
    public NumberingLevel? ResolveLevel(int numberingId, int level) => ResolveLevel(numberingId, level, depth: 0);

    private AbstractNumbering? ResolveDefinition(int numberingId, int depth)
    {
        NumberingInstance? instance = Instances.FirstOrDefault(i => i.Id == numberingId);
        if (instance is null)
            return null;

        AbstractNumbering? definition = Definitions.FirstOrDefault(d => d.Id == instance.AbstractId);
        if (definition is null || definition.Levels.Count > 0)
            return definition;

        return Linked(definition, depth) is { } linked ? ResolveDefinition(linked, depth + 1) : definition;
    }

    private NumberingLevel? ResolveLevel(int numberingId, int level, int depth)
    {
        NumberingInstance? instance = Instances.FirstOrDefault(i => i.Id == numberingId);
        if (instance is null)
            return null;

        if (instance.Overrides.FirstOrDefault(o => o.Level == level)?.Definition is { } overridden)
            return overridden;

        AbstractNumbering? definition = Definitions.FirstOrDefault(d => d.Id == instance.AbstractId);
        if (definition is null)
            return null;

        if (definition.Levels.FirstOrDefault(l => l.Level == level) is { } declared)
            return declared;

        return Linked(definition, depth) is { } linked ? ResolveLevel(linked, level, depth + 1) : null;
    }

    /// <summary>
    /// The list instance a definition defers to through its <c>w:numStyleLink</c>
    /// (ISO/IEC 29500-1 §17.9.21), or <see langword="null"/> when it defers to nothing.
    /// </summary>
    /// <remarks>
    /// Such a definition holds no levels of its own: it names a numbering style, and that
    /// style's <c>w:numPr/w:numId</c> points at the instance whose definition does. A chain of
    /// these is not something the standard describes, so it is followed a short way and then
    /// abandoned rather than trusted — a file can name a style that links back.
    /// </remarks>
    private int? Linked(AbstractNumbering definition, int depth)
    {
        if (depth >= 4 || definition.NumberingStyleLink is not { } styleId)
            return null;

        return Owner?.Styles.Find(styleId)?.ParagraphFormat.NumberingId;
    }

    /// <summary>Adds a nine-level bullet list and returns the instance id to put on paragraphs.</summary>
    public int AddBulletList() => AddList(ListTemplate.Bullet);

    /// <summary>Adds a nine-level decimal outline and returns the instance id to put on paragraphs.</summary>
    public int AddNumberedList() => AddList(ListTemplate.Decimal);

    /// <summary>Adds a legal-style outline (1, 1.1, 1.1.1) and returns the instance id.</summary>
    public int AddOutlineList() => AddList(ListTemplate.Outline);

    /// <summary>Adds a list built from a template and returns the instance id to put on paragraphs.</summary>
    /// <param name="template">Which preset to build.</param>
    public int AddList(ListTemplate template)
    {
        var definition = new AbstractNumbering
        {
            Id = Definitions.Count == 0 ? 0 : Definitions.Max(static d => d.Id) + 1,
            MultiLevelType = template == ListTemplate.Bullet ? "hybridMultilevel" : "multilevel",
        };

        for (int level = 0; level < 9; level++)
            definition.Levels.Add(ListTemplates.CreateLevel(template, level));

        Definitions.Add(definition);
        var instance = new NumberingInstance
        {
            Id = Instances.Count == 0 ? 1 : Instances.Max(static i => i.Id) + 1,
            AbstractId = definition.Id,
        };

        Instances.Add(instance);
        return instance.Id;
    }

    /// <summary>Returns <see langword="true"/> when nothing is defined and the part can be skipped.</summary>
    public bool IsEmpty =>
        Definitions.Count == 0 && Instances.Count == 0 && PictureBullets.Count == 0 && CleanupXml is null;
}

/// <summary>The list presets <see cref="NumberingDefinitions.AddList"/> can build.</summary>
public enum ListTemplate
{
    /// <summary>Round, hollow and square bullets, cycling by depth.</summary>
    Bullet,

    /// <summary>Numbers, letters and roman numerals, restarting at each level.</summary>
    Decimal,

    /// <summary>Legal outline numbering: 1, 1.1, 1.1.1.</summary>
    Outline,
}

/// <summary>Builds the levels of the list presets.</summary>
internal static class ListTemplates
{
    private static readonly string[] BulletGlyphs = ["\uF0B7", "o", "\uF0A7"];
    private static readonly string[] BulletFonts = ["Symbol", "Courier New", "Wingdings"];

    private static readonly ListNumberFormat[] DecimalFormats =
        [ListNumberFormat.Decimal, ListNumberFormat.LowerLetter, ListNumberFormat.LowerRoman];

    public static NumberingLevel CreateLevel(ListTemplate template, int level)
    {
        Length indent = Length.FromTwips(720 * (level + 1));
        var result = new NumberingLevel
        {
            Level = level,
            Start = 1,
            ParagraphFormat = ParagraphFormat.Default with
            {
                IndentLeft = indent,
                IndentHanging = Length.FromTwips(360),
            },
        };

        switch (template)
        {
            case ListTemplate.Bullet:
                result.Format = ListNumberFormat.Bullet;
                result.Text = BulletGlyphs[level % BulletGlyphs.Length];
                result.RunFormat = RunFormat.Default with
                {
                    FontAscii = BulletFonts[level % BulletFonts.Length],
                    FontHighAnsi = BulletFonts[level % BulletFonts.Length],
                    FontHint = "default",
                };
                break;

            case ListTemplate.Outline:
                result.Format = ListNumberFormat.Decimal;
                result.Text = string.Join('.', Enumerable.Range(1, level + 1).Select(static i => $"%{i}")) + ".";
                result.ParagraphFormat = result.ParagraphFormat with
                {
                    IndentLeft = Length.FromTwips(432 * (level + 1)),
                    IndentHanging = Length.FromTwips(432),
                };
                break;

            default:
                result.Format = DecimalFormats[level % DecimalFormats.Length];
                result.Text = $"%{level + 1}" + (result.Format == ListNumberFormat.LowerLetter ? ")" : ".");
                break;
        }

        return result;
    }
}
