using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Editing;

/// <summary>
/// A cursor over a document that appends content where it stands.
/// </summary>
/// <remarks>
/// The document model is a tree, and building a report through it means holding on to the
/// container, the paragraph and the formatting by hand. The editor keeps that state instead:
/// it knows where it is, what formatting is in force, and appends to whichever container it
/// was last moved into.
/// </remarks>
public sealed class DocumentEditor
{
    private BlockContainer _container;
    private Paragraph? _paragraph;

    /// <summary>Creates an editor positioned at the end of a document's first section.</summary>
    /// <param name="document">Document to edit.</param>
    public DocumentEditor(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        _container = document.Sections[0];
    }

    /// <summary>The document being edited.</summary>
    public WordDocument Document { get; }

    /// <summary>The container new content goes into.</summary>
    public BlockContainer Container => _container;

    /// <summary>Character formatting applied to text written from now on.</summary>
    public RunFormat Format { get; set; } = RunFormat.Default;

    /// <summary>The paragraph the cursor is in, creating one when there is none.</summary>
    public Paragraph CurrentParagraph => _paragraph ??= _container.AddParagraph();

    /// <summary>Moves into a section.</summary>
    /// <param name="index">Zero-based section index.</param>
    public DocumentEditor MoveToSection(int index) => MoveTo(Document.Sections[index]);

    /// <summary>Moves into a header, creating it when the section has none.</summary>
    /// <param name="kind">Which header slot.</param>
    public DocumentEditor MoveToHeader(HeaderFooterKind kind = HeaderFooterKind.Default) =>
        MoveTo(CurrentSection().Headers.GetOrCreate(kind));

    /// <summary>Moves into a footer, creating it when the section has none.</summary>
    /// <param name="kind">Which footer slot.</param>
    public DocumentEditor MoveToFooter(HeaderFooterKind kind = HeaderFooterKind.Default) =>
        MoveTo(CurrentSection().Footers.GetOrCreate(kind));

    /// <summary>Moves into a table cell.</summary>
    /// <param name="cell">The cell to write into.</param>
    public DocumentEditor MoveTo(TableCell cell) => MoveTo((BlockContainer)cell);

    /// <summary>Moves into a container, positioning after its last paragraph.</summary>
    /// <param name="container">The container to write into.</param>
    public DocumentEditor MoveTo(BlockContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        _container = container;
        _paragraph = container.Blocks.Count > 0 ? container.Blocks[^1] as Paragraph : null;
        return this;
    }

    /// <summary>Moves to the paragraph holding a bookmark.</summary>
    /// <param name="name">Bookmark name.</param>
    /// <returns><see langword="true"/> when the bookmark was found.</returns>
    public bool MoveToBookmark(string name)
    {
        foreach (BlockContainer container in Document.AllContainers)
        {
            foreach (Paragraph paragraph in container.Blocks.Paragraphs)
            {
                if (!paragraph.Marks.Any(entry => entry.Mark is BookmarkStart start && start.Name == name))
                    continue;

                _container = container;
                _paragraph = paragraph;
                return true;
            }
        }

        return false;
    }

    /// <summary>Starts a new paragraph and moves into it.</summary>
    /// <param name="styleId">Style to apply, or <see langword="null"/> to keep the default.</param>
    public DocumentEditor StartParagraph(string? styleId = null)
    {
        _paragraph = _container.AddParagraph();
        if (styleId is not null)
        {
            _paragraph.Format = _paragraph.Format with { StyleId = styleId };
            Document.Styles.GetOrAdd(styleId);
        }

        return this;
    }

    /// <summary>Writes text into the current paragraph.</summary>
    /// <param name="text">Text to write.</param>
    public DocumentEditor Write(string text)
    {
        CurrentParagraph.AppendText(text, Format);
        return this;
    }

    /// <summary>Writes text and starts a new paragraph.</summary>
    /// <param name="text">Text to write.</param>
    /// <param name="styleId">Style of the paragraph the text goes into.</param>
    public DocumentEditor WriteLine(string text = "", string? styleId = null)
    {
        if (styleId is not null)
            StartParagraph(styleId);
        Write(text);
        _paragraph = null;
        return this;
    }

    /// <summary>Writes a heading and returns to the default style.</summary>
    /// <param name="text">Heading text.</param>
    /// <param name="level">Heading level, one to nine.</param>
    public DocumentEditor WriteHeading(string text, int level = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 9);
        return WriteLine(text, $"Heading{level}");
    }

    /// <summary>Writes a picture into the current paragraph.</summary>
    /// <param name="image">The image to show.</param>
    /// <param name="width">Rendered width, or <see langword="null"/> for the natural width.</param>
    /// <param name="height">Rendered height, or <see langword="null"/> for the natural height.</param>
    public DocumentEditor WritePicture(ImageData image, Length? width = null, Length? height = null)
    {
        CurrentParagraph.AppendPicture(image, width, height, Format);
        return this;
    }

    /// <summary>Inserts a page break and starts a new paragraph.</summary>
    public DocumentEditor InsertPageBreak()
    {
        CurrentParagraph.AppendBreak(BreakKind.Page, Format);
        _paragraph = null;
        return this;
    }

    /// <summary>Inserts a table and leaves the cursor after it.</summary>
    /// <param name="rows">Number of rows.</param>
    /// <param name="columns">Number of columns.</param>
    public Table InsertTable(int rows, int columns)
    {
        Table table = _container.AddTable(rows, columns);
        _paragraph = null;
        return table;
    }

    /// <summary>Applies a formatting change to text written from now on.</summary>
    /// <param name="transform">Produces the new formatting from the old.</param>
    public DocumentEditor WithFormat(Func<RunFormat, RunFormat> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        Format = transform(Format);
        return this;
    }

    /// <summary>Restores the default formatting for text written from now on.</summary>
    public DocumentEditor ResetFormat()
    {
        Format = RunFormat.Default;
        return this;
    }

    private Section CurrentSection() => _container as Section
        ?? Document.Sections.FirstOrDefault(section => ReferenceEquals(section, _container))
        ?? Document.Sections[0];
}
