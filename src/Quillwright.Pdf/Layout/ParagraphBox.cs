using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// A paragraph after it has been measured: its lines, how much room it wants around it, and the
/// decoration that has to be drawn behind and around whatever part of it lands on a page.
/// </summary>
internal sealed class ParagraphBox : BlockBox
{
    /// <summary>The paragraph this came from.</summary>
    public required Paragraph Source { get; init; }

    /// <summary>Its formatting, after the whole style chain.</summary>
    public required ParagraphFormat Format { get; init; }

    /// <summary>The lines, in order.</summary>
    public required List<LineBox> Lines { get; init; }

    /// <summary>The indent from the container's leading edge, which is where a border sits.</summary>
    public double IndentLeft { get; init; }

    /// <summary>The indent from the container's trailing edge.</summary>
    public double IndentRight { get; init; }

    /// <summary>How wide the container was when this was measured.</summary>
    public double ContainerWidth { get; init; }

    /// <summary>Whether the paragraph opens a page of its own.</summary>
    public bool PageBreakBefore { get; init; }

    /// <summary>Whether the paragraph must stay on the page of the one that follows it.</summary>
    public bool KeepWithNext { get; init; }

    /// <summary>Whether every line of the paragraph must land on one page.</summary>
    public bool KeepLinesTogether { get; init; }

    /// <summary>Whether a single line may be left behind at a page break.</summary>
    public bool WidowControl { get; init; }

    /// <summary>Whether the space between this paragraph and a neighbour of the same style is dropped.</summary>
    public bool ContextualSpacing { get; init; }

    /// <summary>The background behind the paragraph, or <see langword="null"/>.</summary>
    public Shading? Shading { get; init; }

    /// <summary>The border box around the paragraph, or <see langword="null"/>.</summary>
    public BorderSet? Borders { get; init; }

    /// <summary>
    /// The pictures anchored in the paragraph that do not flow with it. They take no room in the
    /// lines; the composer places them against the page once it knows where the paragraph landed.
    /// </summary>
    public List<Picture> Floats { get; } = [];

    /// <summary>The text boxes anchored in the paragraph that do not flow with it.</summary>
    public List<Shape> FloatingShapes { get; } = [];

    /// <summary>
    /// The marker the first line opened with, kept so that measuring the paragraph again — when
    /// a float turns out to overlap it — replays the same marker instead of counting the list on.
    /// </summary>
    public IReadOnlyList<InlineItem>? PrefixItems { get; set; }

    /// <summary>
    /// The note marks the paragraph resolved, in the order its references were met. A second
    /// measurement replays these, so a footnote is neither renumbered nor registered twice.
    /// </summary>
    public List<NoteMark?> NoteMarks { get; set; } = [];

    /// <summary>How tall the lines are together, without the spacing around them.</summary>
    public override double ContentHeight
    {
        get
        {
            double total = 0;
            for (int i = 0; i < Lines.Count;)
            {
                int end = LineRows.End(Lines, i);
                total += Lines[i].Lead + LineRows.Height(Lines, i, end);
                i = end;
            }

            return total;
        }
    }
}
