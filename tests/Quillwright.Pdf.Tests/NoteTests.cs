using System.Text;
using Inkwright.Text;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// Footnotes and endnotes: the mark in the text, the note at the foot of the page it belongs to,
/// and the room the note takes out of the page before the text is allowed to fill it.
/// </summary>
public sealed class NoteTests
{
    private const string Sentence =
        "The quick brown fox jumps over the lazy dog while the cooper mends the barrel by the river. ";

    /// <summary>A section whose lines are exactly twenty points tall, so a test can count them.</summary>
    private static ParagraphFormat FixedLines => ParagraphFormat.Default with
    {
        LineSpacingRule = LineSpacingRule.Exact,
        LineSpacing = Length.FromPoints(20),
    };

    private static (WordDocument Document, Paragraph Body) WithFootnote(string text, string note)
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph(text);
        document.AddFootnote(paragraph, note);
        return (document, paragraph);
    }

    private static IReadOnlyList<string> Lines(Rendered rendered, int page = 0) => rendered.Lines(page);

    [Fact]
    public void AFootnoteMarkStandsInTheTextAndAgainAtTheNote()
    {
        (WordDocument document, _) = WithFootnote("A claim.", "The evidence.");

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<string> lines = Lines(rendered);

        Assert.Equal("A claim.1", lines[0]);
        Assert.Equal("1 The evidence.", lines[^1]);
    }

    [Fact]
    public void TheMarkIsSuperscript()
    {
        (WordDocument document, _) = WithFootnote("A claim.", "The evidence.");

        using Rendered rendered = Rendered.Of(document);
        PdfLetter body = rendered.Letters().First(letter => letter.Text == "A");
        PdfLetter mark = rendered.Letters().First(letter => letter.Text == "1");

        Assert.True(mark.FontSize < body.FontSize, "the mark was not reduced");
        Assert.True(mark.Origin.Y > body.Origin.Y, "the mark was not raised");
    }

    [Fact]
    public void TheNoteSitsAtTheFootOfThePage()
    {
        (WordDocument document, _) = WithFootnote("A claim.", "The evidence.");

        using Rendered rendered = Rendered.Of(document);

        double body = rendered.Letters().First(letter => letter.Text == "A").Origin.Y;
        double note = rendered.Letters()
            .Where(letter => letter.Text == "T")
            .Min(letter => letter.Origin.Y);

        double bottom = document.Sections[0].Properties.Margins.Bottom.Points;
        Assert.True(note < body - 500, "the note was not pushed to the foot of the page");
        Assert.True(note >= bottom, "the note ran past the bottom margin");
    }

    [Fact]
    public void ARuleSeparatesTheNotesFromTheText()
    {
        (WordDocument document, _) = WithFootnote("A claim.", "The evidence.");

        using Rendered rendered = Rendered.Of(document);
        string content = Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());

        Assert.Contains("\nS\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void NotesCountUpThroughTheDocument()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];

        foreach (string claim in new[] { "First.", "Second.", "Third." })
            document.AddFootnote(section.AddParagraph(claim), "Note on " + claim);

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<string> lines = Lines(rendered);

        Assert.Equal("First.1", lines[0]);
        Assert.Equal("Second.2", lines[1]);
        Assert.Equal("Third.3", lines[2]);
        Assert.Contains("3 Note on Third.", lines);
    }

    /// <summary>
    /// The point of the whole exercise: a line that owes a note needs room for the note as well as
    /// for itself, so filling the page to the last line and then adding a note is not allowed.
    /// </summary>
    [Fact]
    public void ALineThatCannotFitWithItsNoteTakesTheNoteToTheNextPage()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];

        // Thirty-four lines of twenty points fill an A4 page exactly.
        for (int i = 0; i < 33; i++)
            section.AddParagraph("Filler line " + i).Format = FixedLines;

        Paragraph last = section.AddParagraph("The claim.");
        last.Format = FixedLines;
        document.AddFootnote(last, "The evidence.");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(2, rendered.PageCount);
        Assert.DoesNotContain("The claim.", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains("The claim.", rendered.Text(1), StringComparison.Ordinal);
        Assert.Contains("The evidence.", rendered.Text(1), StringComparison.Ordinal);
    }

    [Fact]
    public void EachNoteIsPrintedOnThePageItsMarkIsOn()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];

        Paragraph first = section.AddParagraph("Early claim.");
        first.Format = FixedLines;
        document.AddFootnote(first, "Early evidence.");

        for (int i = 0; i < 60; i++)
            section.AddParagraph("Filler line " + i).Format = FixedLines;

        Paragraph second = section.AddParagraph("Late claim.");
        second.Format = FixedLines;
        document.AddFootnote(second, "Late evidence.");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(2, rendered.PageCount);
        Assert.Contains("Early evidence.", rendered.Text(0), StringComparison.Ordinal);
        Assert.DoesNotContain("Late evidence.", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains("Late evidence.", rendered.Text(1), StringComparison.Ordinal);
        Assert.DoesNotContain("Early evidence.", rendered.Text(1), StringComparison.Ordinal);
    }

    [Fact]
    public void ANoteShortensThePageForTheTextAboveIt()
    {
        int FillerOnFirstPage(bool withNote)
        {
            WordDocument document = WordDocument.Create();
            Section section = document.Sections[0];

            Paragraph first = section.AddParagraph("Opening.");
            first.Format = FixedLines;
            if (withNote)
                document.AddFootnote(first, "Evidence.");

            for (int i = 0; i < 60; i++)
                section.AddParagraph("Filler line " + i).Format = FixedLines;

            using Rendered rendered = Rendered.Of(document);
            return rendered.Lines(0).Count(line => line.StartsWith("Filler", StringComparison.Ordinal));
        }

        Assert.True(
            FillerOnFirstPage(withNote: true) < FillerOnFirstPage(withNote: false),
            "the note took no room out of the page");
    }

    [Fact]
    public void EndnotesAreCollectedAtTheEnd()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];

        document.AddEndnote(section.AddParagraph("A claim."), "The evidence.");
        section.AddParagraph("More prose.");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(2, rendered.PageCount);
        Assert.Contains("A claim.", rendered.Text(0), StringComparison.Ordinal);
        Assert.DoesNotContain("The evidence.", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains("The evidence.", rendered.Text(1), StringComparison.Ordinal);
    }

    [Fact]
    public void EndnotesAreNumberedInRomanByDefault()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];

        document.AddEndnote(section.AddParagraph("First."), "First note.");
        document.AddEndnote(section.AddParagraph("Second."), "Second note.");

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<string> lines = Lines(rendered);

        Assert.Equal("First.i", lines[0]);
        Assert.Equal("Second.ii", lines[1]);
    }

    [Fact]
    public void FootnotesAndEndnotesCountSeparately()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];

        Paragraph paragraph = section.AddParagraph("Both.");
        document.AddFootnote(paragraph, "Under the page.");
        document.AddEndnote(paragraph, "At the end.");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal("Both.1i", Lines(rendered)[0]);
    }

    /// <summary>The document may say how its notes are numbered, and a section may disagree.</summary>
    [Fact]
    public void TheDocumentDecidesHowNotesAreNumbered()
    {
        WordDocument document = WordDocument.Create();
        document.Settings.SetRaw("footnotePr", "<w:footnotePr><w:numFmt w:val=\"upperLetter\"/><w:numStart w:val=\"3\"/></w:footnotePr>");

        Section section = document.Sections[0];
        document.AddFootnote(section.AddParagraph("A claim."), "The evidence.");
        document.AddFootnote(section.AddParagraph("Another."), "More evidence.");

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<string> lines = Lines(rendered);

        Assert.Equal("A claim.C", lines[0]);
        Assert.Equal("Another.D", lines[1]);
    }

    [Fact]
    public void ASectionCanOverrideTheDocument()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Properties.FootnotePropertiesXml =
            "<w:footnotePr><w:numFmt w:val=\"lowerRoman\"/></w:footnotePr>";

        document.AddFootnote(document.Sections[0].AddParagraph("A claim."), "The evidence.");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal("A claim.i", Lines(rendered)[0]);
    }

    [Fact]
    public void ANoteInsideATableIsCountedOnce()
    {
        WordDocument document = WordDocument.Create();
        Table table = document.Sections[0].AddTable(1, 2);
        Paragraph inside = table[0, 0].Blocks.Paragraphs.First();
        inside.AppendText("Claim.");
        document.AddFootnote(inside, "Evidence.");
        table[0, 1].SetText("Beside it.");

        using Rendered rendered = Rendered.Of(document);

        // Measuring a table's columns lays its cells out repeatedly; the note must not count each
        // rehearsal, or the mark would read some number far above one.
        Assert.Contains("Claim.1", Lines(rendered)[0], StringComparison.Ordinal);
        Assert.Contains("1 Evidence.", Lines(rendered)[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void ANoteWithSeveralParagraphsIsPrintedWhole()
    {
        WordDocument document = WordDocument.Create();
        Note note = document.AddFootnote(document.Sections[0].AddParagraph("A claim."), "First part.");
        note.AddParagraph("Second part.");

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains("First part.", rendered.Text(), StringComparison.Ordinal);
        Assert.Contains("Second part.", rendered.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentWithNoNotesDrawsNoSeparator()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Nothing to footnote.");

        using Rendered rendered = Rendered.Of(document);
        string content = Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());

        Assert.DoesNotContain("\nS\n", content, StringComparison.Ordinal);
    }
}
