using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Formats;

/// <summary>
/// Translation between the model's enumerations and the attribute values WordprocessingML
/// spells them with. Both directions live together so a value can never be written in a
/// spelling the reader would not recognise.
/// </summary>
internal static class OoxmlEnums
{
    /// <summary>Names a <c>ST_Jc</c> value.</summary>
    /// <param name="value">The alignment.</param>
    /// <param name="strict">
    /// Whether the Strict spelling is wanted. Strict dropped <c>left</c> and <c>right</c> from
    /// the enumeration in favour of <c>start</c> and <c>end</c>, so the Transitional spelling
    /// is not merely old there — it is not a member of the type.
    /// </param>
    public static string Name(ParagraphAlignment value, bool strict = false) => value switch
    {
        ParagraphAlignment.Center => "center",
        ParagraphAlignment.Right => strict ? "end" : "right",
        ParagraphAlignment.Justify => "both",
        ParagraphAlignment.Distribute => "distribute",
        ParagraphAlignment.ThaiDistribute => "thaiDistribute",
        ParagraphAlignment.LowKashida => "lowKashida",
        ParagraphAlignment.MediumKashida => "mediumKashida",
        ParagraphAlignment.HighKashida => "highKashida",
        ParagraphAlignment.NumTab => "numTab",
        _ => strict ? "start" : "left",
    };

    public static ParagraphAlignment? ParseAlignment(string? value) => value switch
    {
        "left" or "start" => ParagraphAlignment.Left,
        "center" => ParagraphAlignment.Center,
        "right" or "end" => ParagraphAlignment.Right,
        "both" => ParagraphAlignment.Justify,
        "distribute" => ParagraphAlignment.Distribute,
        "thaiDistribute" => ParagraphAlignment.ThaiDistribute,
        "lowKashida" => ParagraphAlignment.LowKashida,
        "mediumKashida" => ParagraphAlignment.MediumKashida,
        "highKashida" => ParagraphAlignment.HighKashida,
        "numTab" => ParagraphAlignment.NumTab,
        _ => null,
    };

    public static string Name(LineSpacingRule value) => value switch
    {
        LineSpacingRule.Exact => "exact",
        LineSpacingRule.AtLeast => "atLeast",
        _ => "auto",
    };

    public static LineSpacingRule? ParseLineRule(string? value) => value switch
    {
        "exact" => LineSpacingRule.Exact,
        "atLeast" => LineSpacingRule.AtLeast,
        "auto" => LineSpacingRule.Auto,
        _ => null,
    };

    private static readonly string[] UnderlineNames =
    [
        "none", "single", "words", "double", "thick", "dotted", "dottedHeavy", "dash", "dashedHeavy",
        "dashLong", "dashLongHeavy", "dotDash", "dashDotHeavy", "dotDotDash", "dashDotDotHeavy",
        "wave", "wavyHeavy", "wavyDouble",
    ];

    public static string Name(UnderlineStyle value) => UnderlineNames[(int)value];

    public static UnderlineStyle? ParseUnderline(string? value)
    {
        int index = value is null ? -1 : Array.IndexOf(UnderlineNames, value);
        return index < 0 ? null : (UnderlineStyle)index;
    }

    private static readonly string[] HighlightNames =
    [
        "none", "black", "blue", "cyan", "green", "magenta", "red", "yellow", "white",
        "darkBlue", "darkCyan", "darkGreen", "darkMagenta", "darkRed", "darkYellow", "darkGray", "lightGray",
    ];

    public static string Name(HighlightColor value) => HighlightNames[(int)value];

    public static HighlightColor? ParseHighlight(string? value)
    {
        int index = value is null ? -1 : Array.IndexOf(HighlightNames, value);
        return index < 0 ? null : (HighlightColor)index;
    }

    public static string Name(VerticalTextAlignment value) => value switch
    {
        VerticalTextAlignment.Superscript => "superscript",
        VerticalTextAlignment.Subscript => "subscript",
        _ => "baseline",
    };

    public static VerticalTextAlignment? ParseVerticalAlign(string? value) => value switch
    {
        "superscript" => VerticalTextAlignment.Superscript,
        "subscript" => VerticalTextAlignment.Subscript,
        "baseline" => VerticalTextAlignment.Baseline,
        _ => null,
    };

    private static readonly string[] BorderNames =
    [
        "nil", "none", "single", "thick", "double", "dotted", "dashed", "dotDash", "dotDotDash", "triple",
        "thinThickSmallGap", "thickThinSmallGap", "thinThickThinSmallGap", "thinThickMediumGap",
        "thickThinMediumGap", "thinThickThinMediumGap", "thinThickLargeGap", "thickThinLargeGap",
        "thinThickThinLargeGap", "wave", "doubleWave", "dashSmallGap", "dashDotStroked",
        "threeDEmboss", "threeDEngrave", "outset", "inset",
    ];

    public static string Name(BorderStyle value, string? custom) =>
        value == BorderStyle.Custom ? custom ?? "single" : BorderNames[(int)value];

    public static (BorderStyle Style, string? Custom) ParseBorder(string? value)
    {
        int index = value is null ? -1 : Array.IndexOf(BorderNames, value);
        return index >= 0 ? ((BorderStyle)index, null) : (BorderStyle.Custom, value);
    }

    private static readonly string[] ShadingNames =
    [
        "nil", "clear", "solid", "horzStripe", "vertStripe", "diagStripe", "reverseDiagStripe",
        "horzCross", "diagCross",
    ];

    public static string Name(ShadingPattern value, string? custom) =>
        value == ShadingPattern.Custom ? custom ?? "clear" : ShadingNames[(int)value];

    public static (ShadingPattern Pattern, string? Custom) ParseShading(string? value)
    {
        int index = value is null ? -1 : Array.IndexOf(ShadingNames, value);
        return index >= 0 ? ((ShadingPattern)index, null) : (ShadingPattern.Custom, value);
    }

    /// <summary>Names a <c>ST_TabJc</c> value, which Strict renamed the same way as <c>ST_Jc</c>.</summary>
    /// <param name="value">The tab alignment.</param>
    /// <param name="strict">Whether the Strict spelling is wanted.</param>
    public static string Name(TabAlignment value, bool strict = false) => value switch
    {
        TabAlignment.Center => "center",
        TabAlignment.Right => strict ? "end" : "right",
        TabAlignment.Decimal => "decimal",
        TabAlignment.Bar => "bar",
        TabAlignment.Clear => "clear",
        TabAlignment.Number => "num",
        _ => strict ? "start" : "left",
    };

    public static TabAlignment ParseTabAlignment(string? value) => value switch
    {
        "center" => TabAlignment.Center,
        "right" or "end" => TabAlignment.Right,
        "decimal" => TabAlignment.Decimal,
        "bar" => TabAlignment.Bar,
        "clear" => TabAlignment.Clear,
        "num" => TabAlignment.Number,
        _ => TabAlignment.Left,
    };

    private static readonly string[] TabLeaderNames = ["none", "dot", "hyphen", "underscore", "heavy", "middleDot"];

    public static string Name(TabLeader value) => TabLeaderNames[(int)value];

    public static TabLeader ParseTabLeader(string? value)
    {
        int index = value is null ? -1 : Array.IndexOf(TabLeaderNames, value);
        return index < 0 ? TabLeader.None : (TabLeader)index;
    }

    public static string Name(VerticalCellAlignment value) => value switch
    {
        VerticalCellAlignment.Center => "center",
        VerticalCellAlignment.Both => "both",
        VerticalCellAlignment.Bottom => "bottom",
        _ => "top",
    };

    public static VerticalCellAlignment? ParseCellAlign(string? value) => value switch
    {
        "center" => VerticalCellAlignment.Center,
        "both" => VerticalCellAlignment.Both,
        "bottom" => VerticalCellAlignment.Bottom,
        "top" => VerticalCellAlignment.Top,
        _ => null,
    };

    public static string Name(HeightRule value) => value switch
    {
        HeightRule.Exact => "exact",
        HeightRule.AtLeast => "atLeast",
        _ => "auto",
    };

    public static HeightRule? ParseHeightRule(string? value) => value switch
    {
        "exact" => HeightRule.Exact,
        "atLeast" => HeightRule.AtLeast,
        "auto" => HeightRule.Auto,
        _ => null,
    };

    public static string Name(WidthUnit value) => value switch
    {
        WidthUnit.Twips => "dxa",
        WidthUnit.Percent => "pct",
        WidthUnit.None => "nil",
        _ => "auto",
    };

    public static WidthUnit ParseWidthUnit(string? value) => value switch
    {
        "dxa" => WidthUnit.Twips,
        "pct" => WidthUnit.Percent,
        "nil" => WidthUnit.None,
        _ => WidthUnit.Auto,
    };

    private static readonly string[] TextDirectionNames = ["lrTb", "tbRl", "tbLrV", "btLr", "lrTbV", "tbRlV"];

    public static string Name(TextDirection value) => TextDirectionNames[(int)value];

    public static TextDirection? ParseTextDirection(string? value)
    {
        int index = value is null ? -1 : Array.IndexOf(TextDirectionNames, value);
        return index < 0 ? null : (TextDirection)index;
    }

    public static string Name(SectionStart value) => value switch
    {
        SectionStart.Continuous => "continuous",
        SectionStart.NextColumn => "nextColumn",
        SectionStart.EvenPage => "evenPage",
        SectionStart.OddPage => "oddPage",
        _ => "nextPage",
    };

    public static SectionStart ParseSectionStart(string? value) => value switch
    {
        "continuous" => SectionStart.Continuous,
        "nextColumn" => SectionStart.NextColumn,
        "evenPage" => SectionStart.EvenPage,
        "oddPage" => SectionStart.OddPage,
        _ => SectionStart.NextPage,
    };

    private static readonly string[] NumberFormatNames =
    [
        "decimal", "lowerRoman", "upperRoman", "lowerLetter", "upperLetter", "ordinal", "cardinalText",
        "ordinalText", "bullet", "decimalZero", "none", "russianLower", "russianUpper",
        "decimalEnclosedCircle", "decimalFullWidth",
    ];

    public static string Name(ListNumberFormat value, string? custom) =>
        value == ListNumberFormat.Custom ? custom ?? "decimal" : NumberFormatNames[(int)value];

    public static (ListNumberFormat Format, string? Custom) ParseNumberFormat(string? value)
    {
        int index = value is null ? -1 : Array.IndexOf(NumberFormatNames, value);
        return index >= 0 ? ((ListNumberFormat)index, null) : (ListNumberFormat.Custom, value);
    }

    public static string Name(ListLevelSuffix value) => value switch
    {
        ListLevelSuffix.Space => "space",
        ListLevelSuffix.Nothing => "nothing",
        _ => "tab",
    };

    public static ListLevelSuffix ParseLevelSuffix(string? value) => value switch
    {
        "space" => ListLevelSuffix.Space,
        "nothing" => ListLevelSuffix.Nothing,
        _ => ListLevelSuffix.Tab,
    };

    public static string Name(TableLayout value) => value == TableLayout.Fixed ? "fixed" : "autofit";

    public static TableLayout? ParseTableLayout(string? value) => value switch
    {
        "fixed" => TableLayout.Fixed,
        "autofit" => TableLayout.AutoFit,
        _ => null,
    };

    /// <summary>Names a <c>ST_JcTable</c> value, which Strict renamed the same way as <c>ST_Jc</c>.</summary>
    /// <param name="value">The table alignment.</param>
    /// <param name="strict">Whether the Strict spelling is wanted.</param>
    public static string Name(TableAlignment value, bool strict = false) => value switch
    {
        TableAlignment.Center => "center",
        TableAlignment.Right => strict ? "end" : "right",
        _ => strict ? "start" : "left",
    };

    public static TableAlignment? ParseTableAlignment(string? value) => value switch
    {
        "center" => TableAlignment.Center,
        "right" or "end" => TableAlignment.Right,
        "left" or "start" => TableAlignment.Left,
        _ => null,
    };

    public static string Name(DocumentProtection value) => value switch
    {
        DocumentProtection.TrackedChanges => "trackedChanges",
        DocumentProtection.Comments => "comments",
        DocumentProtection.Forms => "forms",
        DocumentProtection.ReadOnly => "readOnly",
        _ => "none",
    };

    public static DocumentProtection ParseDocumentProtection(string? value) => value switch
    {
        "trackedChanges" => DocumentProtection.TrackedChanges,
        "comments" => DocumentProtection.Comments,
        "forms" => DocumentProtection.Forms,
        "readOnly" => DocumentProtection.ReadOnly,
        _ => DocumentProtection.None,
    };

    public static string Name(StyleKind value) => value switch
    {
        StyleKind.Character => "character",
        StyleKind.Table => "table",
        StyleKind.Numbering => "numbering",
        _ => "paragraph",
    };

    public static StyleKind ParseStyleKind(string? value) => value switch
    {
        "character" => StyleKind.Character,
        "table" => StyleKind.Table,
        "numbering" => StyleKind.Numbering,
        _ => StyleKind.Paragraph,
    };

    private static readonly string[] RegionNames =
    [
        "wholeTable", "firstRow", "lastRow", "firstCol", "lastCol", "band1Horz", "band2Horz",
        "band1Vert", "band2Vert", "nwCell", "neCell", "swCell", "seCell",
    ];

    public static string Name(TableStyleRegion value) => RegionNames[(int)value];

    public static TableStyleRegion? ParseRegion(string? value)
    {
        int index = value is null ? -1 : Array.IndexOf(RegionNames, value);
        return index < 0 ? null : (TableStyleRegion)index;
    }

    public static string Name(BreakKind value) => value switch
    {
        BreakKind.Page => "page",
        BreakKind.Column => "column",
        _ => "textWrapping",
    };

    public static BreakKind ParseBreakKind(string? value) => value switch
    {
        "page" => BreakKind.Page,
        "column" => BreakKind.Column,
        _ => BreakKind.Line,
    };

    public static string Name(BreakClear value) => value switch
    {
        BreakClear.Left => "left",
        BreakClear.Right => "right",
        BreakClear.All => "all",
        _ => "none",
    };

    public static BreakClear ParseBreakClear(string? value) => value switch
    {
        "left" => BreakClear.Left,
        "right" => BreakClear.Right,
        "all" => BreakClear.All,
        _ => BreakClear.None,
    };

    public static string Name(FieldCharKind value) => value switch
    {
        FieldCharKind.Separate => "separate",
        FieldCharKind.End => "end",
        _ => "begin",
    };

    public static FieldCharKind ParseFieldCharKind(string? value) => value switch
    {
        "separate" => FieldCharKind.Separate,
        "end" => FieldCharKind.End,
        _ => FieldCharKind.Begin,
    };

    public static string Name(LineTextAlignment value) => value switch
    {
        LineTextAlignment.Baseline => "baseline",
        LineTextAlignment.Bottom => "bottom",
        LineTextAlignment.Center => "center",
        LineTextAlignment.Top => "top",
        _ => "auto",
    };

    public static LineTextAlignment? ParseLineTextAlignment(string? value) => value switch
    {
        "baseline" => LineTextAlignment.Baseline,
        "bottom" => LineTextAlignment.Bottom,
        "center" => LineTextAlignment.Center,
        "top" => LineTextAlignment.Top,
        "auto" => LineTextAlignment.Auto,
        _ => null,
    };

    public static string Name(RevisionKind value) => value switch
    {
        RevisionKind.Deleted => "del",
        RevisionKind.MovedFrom => "moveFrom",
        RevisionKind.MovedTo => "moveTo",
        _ => "ins",
    };

    public static RevisionKind? ParseRevisionKind(string? value) => value switch
    {
        "ins" => RevisionKind.Inserted,
        "del" => RevisionKind.Deleted,
        "moveFrom" => RevisionKind.MovedFrom,
        "moveTo" => RevisionKind.MovedTo,
        _ => null,
    };
}
