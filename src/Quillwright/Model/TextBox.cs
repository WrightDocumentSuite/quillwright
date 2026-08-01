namespace Quillwright.Model;

/// <summary>
/// The content of a text box: paragraphs and tables, like any other container.
/// </summary>
/// <remarks>
/// A text box is a shape with words in it, and the words are ordinary WordprocessingML held in
/// a <c>w:txbxContent</c> element. Modelling them as a container is what puts them within
/// reach of <c>GetText</c>, find-and-replace and the templating engine; the shape around them is
/// kept verbatim, and what <see cref="Shape"/> says about its size and position is a reading of
/// those same bytes rather than a second copy that could disagree with them.
/// </remarks>
public sealed class TextBox : BlockContainer
{
    /// <inheritdoc />
    public override WordDocument? Document => Owner?.Host?.Document;

    /// <summary>The shape this is the content of.</summary>
    internal Shape? Owner { get; set; }
}

/// <summary>
/// A shape anchored in the text whose words this version reads: a text box, a callout, a
/// banner with a caption in it.
/// </summary>
/// <remarks>
/// <para>
/// The shape itself is kept as the bytes it arrived as, cut into the pieces that surround its
/// content. Only the content is regenerated, so everything else — the effects, the compatibility
/// branch an older reader falls back to, every attribute nobody here has heard of — survives a
/// round trip exactly.
/// </para>
/// <para>
/// Its size, position, fill and outline are readable but not settable, and that is the point:
/// they are a reading of the markup rather than a second copy of it, so a renderer can draw the
/// shape where it belongs while the bytes written back stay the bytes that were read.
/// </para>
/// <para>
/// Word writes a text box twice, once as a modern drawing and once as a VML picture, and both
/// copies hold the same words. When they arrive identical they are written back identical, so
/// editing the text does not leave the fallback saying something else.
/// </para>
/// </remarks>
public sealed class Shape : InlineObject
{
    /// <summary>Creates a shape around content that has already been read.</summary>
    /// <param name="fragments">
    /// The verbatim markup, cut at the places the content goes: one piece more than there are
    /// places, so the content is written between consecutive pieces.
    /// </param>
    /// <param name="content">The content of the shape.</param>
    public Shape(IReadOnlyList<string> fragments, TextBox content)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        ArgumentNullException.ThrowIfNull(content);
        if (fragments.Count < 2)
            throw new ArgumentException("A shape needs markup on both sides of its content.", nameof(fragments));

        Fragments = fragments;
        Content = content;
        content.Owner = this;
    }

    /// <summary>The verbatim markup around the content.</summary>
    public IReadOnlyList<string> Fragments { get; }

    /// <summary>The words inside the shape.</summary>
    public TextBox Content { get; }

    /// <summary>How wide the shape is drawn, or zero when its markup does not say.</summary>
    public Primitives.Length Width { get; internal init; }

    /// <summary>How tall the shape is drawn, or zero when its markup does not say.</summary>
    public Primitives.Length Height { get; internal init; }

    /// <summary>Whether the shape flows with the text rather than floating.</summary>
    public bool IsInline { get; internal init; } = true;

    /// <summary>
    /// Where a floating shape sits and how the text behaves around it, or <see langword="null"/>
    /// when it flows with the text or its markup says nothing.
    /// </summary>
    public PictureAnchor? Anchor { get; internal init; }

    /// <summary>The background of the shape, or <see langword="null"/> when it has none.</summary>
    public Primitives.WordColor? Fill { get; internal init; }

    /// <summary>Which way the words inside flow: the ordinary way, or down a rotated box.</summary>
    public Styles.TextDirection Direction { get; internal init; }

    /// <summary>The line around the shape, or <see langword="null"/> when it has none.</summary>
    public Styles.BorderLine? Outline { get; internal init; }

    /// <summary>The paragraph the shape sits in, once it has been placed.</summary>
    internal Paragraph? Host { get; set; }

    /// <inheritdoc />
    public override string? GetText() => Content.GetText();
}
