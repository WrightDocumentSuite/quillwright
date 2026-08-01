using Quillwright.Styles;

namespace Quillwright.Model;

/// <summary>How a stretch of run text is written out.</summary>
public enum RunKind : byte
{
    /// <summary>Ordinary visible text (<c>w:t</c>).</summary>
    Text = 0,

    /// <summary>The instruction of a complex field (<c>w:instrText</c>).</summary>
    FieldInstruction,

    /// <summary>Text removed by a tracked deletion (<c>w:delText</c>).</summary>
    Deleted,

    /// <summary>A field instruction inside a tracked deletion (<c>w:delInstrText</c>).</summary>
    DeletedFieldInstruction,
}

/// <summary>One formatted stretch of a paragraph's text buffer.</summary>
internal struct RunSpan
{
    /// <summary>Offset of the first character.</summary>
    public int Start;

    /// <summary>Number of characters covered.</summary>
    public int Length;

    /// <summary>Character formatting, shared with every run that formats the same way.</summary>
    public RunFormat Format;

    /// <summary>How the text is written out.</summary>
    public RunKind Kind;

    /// <summary>Attributes of <c>w:r</c> this version does not model, kept verbatim.</summary>
    public string? Attributes;

    /// <summary>Offset one past the last character.</summary>
    public readonly int End => Start + Length;
}

/// <summary>An object anchored at one character of the text buffer.</summary>
internal struct AnchoredObject
{
    /// <summary>Offset of the placeholder character.</summary>
    public int Offset;

    /// <summary>The object.</summary>
    public InlineObject Object;
}

/// <summary>A zero-width mark anchored between two characters.</summary>
internal struct AnchoredMark
{
    /// <summary>Offset the mark sits before.</summary>
    public int Offset;

    /// <summary>The mark.</summary>
    public InlineMark Mark;
}

/// <summary>A wrapper covering a stretch of the text buffer.</summary>
internal struct AnchoredRange
{
    /// <summary>Offset of the first covered character.</summary>
    public int Start;

    /// <summary>Number of characters covered.</summary>
    public int Length;

    /// <summary>The wrapper.</summary>
    public InlineRange Range;

    /// <summary>Offset one past the last covered character.</summary>
    public readonly int End => Start + Length;
}
