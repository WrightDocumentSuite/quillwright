using Quillwright.Styles;

namespace Quillwright.Model;

/// <summary>
/// The content of a header or a footer. It lives in its own package part and can be shared
/// by several sections, which is how Word implements "link to previous".
/// </summary>
public sealed class HeaderFooter : BlockContainer
{
    /// <summary>Creates a header or footer belonging to a document.</summary>
    /// <param name="document">Owning document.</param>
    /// <param name="isFooter">Whether this is a footer rather than a header.</param>
    internal HeaderFooter(WordDocument document, bool isFooter)
    {
        Owner = document;
        IsFooter = isFooter;
    }

    /// <summary>Whether this is a footer rather than a header.</summary>
    public bool IsFooter { get; }

    /// <inheritdoc />
    public override WordDocument? Document => Owner;

    internal WordDocument Owner { get; }

    /// <summary>Package part the content is stored in; assigned when loaded or saved.</summary>
    internal string? PartPath { get; set; }

    /// <summary>Relationship id the section refers to this part by.</summary>
    internal string? RelationshipId { get; set; }

    /// <summary>Attributes of the root element, kept verbatim.</summary>
    internal string? Attributes { get; set; }
}

/// <summary>
/// The three header or footer slots of a section. A slot left <see langword="null"/> falls
/// back to <see cref="Default"/>, and <see cref="First"/> only applies when the section is
/// marked as having a distinct first page.
/// </summary>
public sealed class HeaderFooterSlots
{
    private readonly Section _section;
    private readonly bool _isFooter;

    internal HeaderFooterSlots(Section section, bool isFooter)
    {
        _section = section;
        _isFooter = isFooter;
    }

    /// <summary>Applies to every page not covered by another slot.</summary>
    public HeaderFooter? Default { get; set; }

    /// <summary>Applies to the first page when <see cref="SectionProperties.DifferentFirstPage"/> is set.</summary>
    public HeaderFooter? First { get; set; }

    /// <summary>Applies to even pages when the document has odd and even headers turned on.</summary>
    public HeaderFooter? Even { get; set; }

    /// <summary>The slot for a kind.</summary>
    /// <param name="kind">Which slot.</param>
    public HeaderFooter? this[HeaderFooterKind kind]
    {
        get => kind switch
        {
            HeaderFooterKind.First => First,
            HeaderFooterKind.Even => Even,
            _ => Default,
        };
        set
        {
            switch (kind)
            {
                case HeaderFooterKind.First:
                    First = value;
                    break;
                case HeaderFooterKind.Even:
                    Even = value;
                    break;
                default:
                    Default = value;
                    break;
            }
        }
    }

    /// <summary>Returns the slot, creating an empty header or footer when it is not set yet.</summary>
    /// <param name="kind">Which slot.</param>
    public HeaderFooter GetOrCreate(HeaderFooterKind kind = HeaderFooterKind.Default)
    {
        if (this[kind] is { } existing)
            return existing;

        WordDocument document = _section.Document
            ?? throw new InvalidOperationException("The section must belong to a document before a header or footer can be created.");

        var created = new HeaderFooter(document, _isFooter);
        document.RegisterHeaderFooter(created);
        this[kind] = created;
        if (kind == HeaderFooterKind.First)
            _section.Properties.DifferentFirstPage = true;
        return created;
    }

    /// <summary>Every slot that is set, in default-first-even order.</summary>
    public IEnumerable<(HeaderFooterKind Kind, HeaderFooter Content)> Defined
    {
        get
        {
            if (Default is { } @default)
                yield return (HeaderFooterKind.Default, @default);
            if (First is { } first)
                yield return (HeaderFooterKind.First, first);
            if (Even is { } even)
                yield return (HeaderFooterKind.Even, even);
        }
    }
}
