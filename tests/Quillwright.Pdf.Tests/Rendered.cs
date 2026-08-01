using Inkwright;
using Inkwright.Text;
using Quillwright.Model;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// Renders a document and reads the result back the way a viewer would: through a saved file, so
/// that anything the writer defers — font subsetting, the <c>ToUnicode</c> map — has happened.
/// </summary>
internal sealed class Rendered : IDisposable
{
    private Rendered(PdfDocument document, PdfExportDiagnostics diagnostics)
    {
        Document = document;
        Diagnostics = diagnostics;
    }

    public PdfDocument Document { get; }

    public PdfExportDiagnostics Diagnostics { get; }

    public int PageCount => Document.Pages.Count;

    /// <summary>Renders a document and reads the result back.</summary>
    /// <param name="document">The document to render.</param>
    /// <param name="options">How to render it.</param>
    /// <param name="before">
    /// What to do to the rendered document before it is saved, which is where a caller would sign
    /// it, encrypt it or claim conformance with a profile.
    /// </param>
    public static Rendered Of(
        WordDocument document, PdfExportOptions? options = null, Action<PdfDocument>? before = null)
    {
        PdfExportResult result = PdfExporter.Render(document, options);
        byte[] bytes;
        using (result.Document)
        {
            before?.Invoke(result.Document);
            bytes = result.Document.ToArray();
        }

        return new Rendered(PdfDocument.Load(bytes), result.Diagnostics);
    }

    public string Text(int page = 0) => Document.Pages[page].ExtractText();

    public IReadOnlyList<PdfLetter> Letters(int page = 0) => Document.Pages[page].ExtractLetters();

    /// <summary>
    /// The lines of a page as a reader sees them, top down.
    /// </summary>
    /// <remarks>
    /// Glyphs are grouped by baseline, but not by an exact one: a superscript sits a few points
    /// above the words it belongs to and is still part of the same line. Anything within a few
    /// points of the glyph before it joins that line, which is far less than the gap between two
    /// real lines of text.
    /// </remarks>
    public IReadOnlyList<string> Lines(int page = 0)
    {
        const double SameLine = 6;

        List<string> lines = [];
        List<PdfLetter> current = [];
        double previous = double.NaN;

        foreach (PdfLetter letter in Letters(page).OrderByDescending(letter => letter.Origin.Y))
        {
            if (current.Count > 0 && previous - letter.Origin.Y > SameLine)
            {
                lines.Add(Join(current));
                current.Clear();
            }

            current.Add(letter);
            previous = letter.Origin.Y;
        }

        if (current.Count > 0)
            lines.Add(Join(current));

        return lines;

        static string Join(List<PdfLetter> letters) =>
            string.Concat(letters.OrderBy(letter => letter.Origin.X).Select(letter => letter.Text));
    }

    /// <summary>The baselines the page draws text on, from the top of the page down.</summary>
    public IReadOnlyList<double> Baselines(int page = 0) =>
        [.. Letters(page)
            .Select(letter => Math.Round(letter.Origin.Y, 2))
            .Distinct()
            .OrderByDescending(y => y)];

    /// <summary>The leftmost point any glyph reaches on a page.</summary>
    public double LeftEdge(int page = 0) => Letters(page).Min(letter => letter.Origin.X);

    /// <summary>The rightmost point any glyph reaches on a page.</summary>
    public double RightEdge(int page = 0) =>
        Letters(page).Max(letter => letter.Origin.X + letter.Width);

    public void Dispose() => Document.Dispose();
}
