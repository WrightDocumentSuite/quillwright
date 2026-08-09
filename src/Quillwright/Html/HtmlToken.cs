using System.Text;

namespace Quillwright.Html;

/// <summary>What the tokenizer emits (WHATWG HTML §13.2.5).</summary>
internal enum HtmlTokenKind : byte
{
    /// <summary>A run of character data.</summary>
    Character,

    /// <summary>A start tag, with its attributes and self-closing flag.</summary>
    StartTag,

    /// <summary>An end tag.</summary>
    EndTag,

    /// <summary>A comment.</summary>
    Comment,

    /// <summary>A processing instruction.</summary>
    ProcessingInstruction,

    /// <summary>A doctype.</summary>
    Doctype,

    /// <summary>The end of the input.</summary>
    EndOfFile,
}

/// <summary>One token, reused by the tokenizer between emissions.</summary>
internal sealed class HtmlToken
{
    /// <summary>What this token is.</summary>
    public HtmlTokenKind Kind { get; set; }

    /// <summary>The tag/doctype name or processing-instruction target, ASCII-folded where required.</summary>
    public StringBuilder Name { get; } = new();

    /// <summary>The characters of a character, comment, or processing-instruction token.</summary>
    public StringBuilder Data { get; } = new();

    /// <summary>The attributes of a start tag, in source order, duplicates already dropped.</summary>
    public List<HtmlAttribute> Attributes { get; } = [];

    /// <summary>Whether the tag ended with a solidus.</summary>
    public bool SelfClosing { get; set; }

    /// <summary>The 1-based source line the token began on.</summary>
    public int Line { get; set; }

    /// <summary>The doctype's public identifier, when it has one.</summary>
    public string? PublicIdentifier { get; set; }

    /// <summary>The doctype's system identifier, when it has one.</summary>
    public string? SystemIdentifier { get; set; }

    /// <summary>Whether the doctype forces quirks mode.</summary>
    public bool ForceQuirks { get; set; }

    /// <summary>Readies the token for reuse as a new token of a kind.</summary>
    /// <param name="kind">What the token will be.</param>
    /// <param name="line">The source line it begins on.</param>
    public void Reset(HtmlTokenKind kind, int line)
    {
        Kind = kind;
        Name.Clear();
        Data.Clear();
        Attributes.Clear();
        SelfClosing = false;
        Line = line;
        PublicIdentifier = null;
        SystemIdentifier = null;
        ForceQuirks = false;
    }

    /// <summary>An independent copy, for a token the tree builder has to keep.</summary>
    public HtmlToken Clone()
    {
        var copy = new HtmlToken
        {
            Kind = Kind,
            SelfClosing = SelfClosing,
            Line = Line,
            PublicIdentifier = PublicIdentifier,
            SystemIdentifier = SystemIdentifier,
            ForceQuirks = ForceQuirks,
        };

        copy.Name.Append(Name);
        copy.Data.Append(Data);
        copy.Attributes.AddRange(Attributes);
        return copy;
    }

    /// <summary>The tag name as a string.</summary>
    public string TagName => Name.ToString();

    /// <summary>The processing instruction target as a string.</summary>
    public string ProcessingInstructionTarget => Name.ToString();
}

/// <summary>One attribute of a start tag.</summary>
/// <param name="Name">The name, with ASCII uppercase characters lower-cased.</param>
/// <param name="Value">The value, character references already expanded.</param>
internal readonly record struct HtmlAttribute(string Name, string Value);
