using Quillwright.Editing;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Tests;

/// <summary>
/// Comparing two documents into a redline. The contract is the one Word's own Compare keeps:
/// the result is the original with tracked changes over it, accepting them all yields the
/// revised text and rejecting them all the original — checked here through the library's own
/// accept and reject, which are proved against Word's semantics elsewhere.
/// </summary>
public class DocumentCompareTests
{
    [Fact]
    public void IdenticalDocuments_CompareClean()
    {
        WordDocument original = Build("Same words.", "On two paragraphs.");
        WordDocument revised = Build("Same words.", "On two paragraphs.");

        ComparisonResult result = DocumentComparer.Compare(original, revised);

        Assert.True(result.AreIdentical);
        Assert.False(result.Document.HasRevisions());
    }

    [Fact]
    public void AChangedWord_AcceptsToRevisedAndRejectsToOriginal()
    {
        AssertRoundTrip(
            original: Build("The quick brown fox jumps over the lazy dog."),
            revised: Build("The quick red fox jumps over the lazy dog."));
    }

    [Fact]
    public void AnAddedSentence_AcceptsAndRejects()
    {
        AssertRoundTrip(
            original: Build("First paragraph.", "Last paragraph."),
            revised: Build("First paragraph.", "A new middle paragraph.", "Last paragraph."));
    }

    [Fact]
    public void ARemovedParagraph_AcceptsAndRejects()
    {
        AssertRoundTrip(
            original: Build("Kept.", "Doomed.", "Also kept."),
            revised: Build("Kept.", "Also kept."));
    }

    [Fact]
    public void EditsAtBothEnds_AcceptAndReject()
    {
        AssertRoundTrip(
            original: Build("Opening words stay.", "The middle got reworked entirely today.", "Closing words stay."),
            revised: Build("Opening words stay.", "The middle was rewritten from scratch.", "Closing words stay."));
    }

    [Fact]
    public void TheDifferences_CarryTheAuthor()
    {
        WordDocument original = Build("The quick brown fox.");
        WordDocument revised = Build("The quick red fox.");

        ComparisonResult result = DocumentComparer.Compare(
            original, revised, new DocumentCompareOptions { Author = "Reviewer" });

        List<Revision> revisions = [.. result.Document.Paragraphs
            .SelectMany(static p => p.Ranges)
            .Select(static r => r.Range)
            .OfType<Revision>()];

        Assert.NotEmpty(revisions);
        Assert.All(revisions, static revision => Assert.Equal("Reviewer", revision.Author));
        Assert.Contains(revisions, static r => r.Kind == RevisionKind.Inserted);
        Assert.Contains(revisions, static r => r.Kind == RevisionKind.Deleted);
    }

    [Fact]
    public void InsertedText_WearsTheRevisedFormatting()
    {
        WordDocument original = Build("Plain words here.");
        WordDocument revised = WordDocument.Create();
        var paragraph = new Paragraph("Plain ");
        paragraph.AppendText("bold ", RunFormat.Default with { Bold = true });
        paragraph.AppendText("words here.");
        revised.Sections[0].Blocks.Add(paragraph);

        ComparisonResult result = DocumentComparer.Compare(original, revised);
        result.Document.AcceptAllRevisions();

        Run bold = result.Document.Paragraphs.First().Runs.Single(static r => r.Text.Contains("bold", StringComparison.Ordinal));
        Assert.True(bold.Format.Bold);
    }

    [Fact]
    public void AChangedTable_IsRecordedRowByRow()
    {
        WordDocument original = WithTable("one", "two");
        WordDocument revised = WithTable("one", "three");

        ComparisonResult accepted = DocumentComparer.Compare(original, revised);
        accepted.Document.AcceptAllRevisions();
        Table table = accepted.Document.Sections[0].Blocks.OfType<Table>().Single();
        Assert.Equal("three", table.Rows[0].Cells[1].GetText());

        ComparisonResult rejected = DocumentComparer.Compare(original, revised);
        rejected.Document.RejectAllRevisions();
        Table restored = rejected.Document.Sections[0].Blocks.OfType<Table>().Single();
        Assert.Equal("two", restored.Rows[0].Cells[1].GetText());
    }

    [Fact]
    public void ARemovedTable_VanishesOnAcceptAndSurvivesReject()
    {
        WordDocument original = WithTable("cell", "cells");
        original.Sections[0].AddParagraph("After the table.");
        WordDocument revised = Build("After the table.");

        ComparisonResult accepted = DocumentComparer.Compare(original, revised);
        accepted.Document.AcceptAllRevisions();
        Assert.Empty(accepted.Document.Sections[0].Blocks.OfType<Table>());

        ComparisonResult rejected = DocumentComparer.Compare(original, revised);
        rejected.Document.RejectAllRevisions();
        Assert.Single(rejected.Document.Sections[0].Blocks.OfType<Table>());
    }

    [Fact]
    public async Task TheRedline_SavesValidAndSurvivesReload()
    {
        WordDocument original = Build("The quick brown fox jumps.", "A second paragraph.");
        WordDocument revised = Build("The quick red fox leaps.", "A second paragraph.", "And a third.");

        ComparisonResult result = DocumentComparer.Compare(original, revised);
        WordDocument reloaded = await DocumentFixture.RoundTripAsync(result.Document, "a comparison result");

        Assert.True(reloaded.HasRevisions());

        reloaded.AcceptAllRevisions();
        Assert.Equal(revised.GetText(), reloaded.GetText());
    }

    [Fact]
    public void TheOriginalAndTheRevised_AreNotChanged()
    {
        WordDocument original = Build("Untouched original.");
        WordDocument revised = Build("Untouched revision.");
        string originalText = original.GetText();
        string revisedText = revised.GetText();

        DocumentComparer.Compare(original, revised);

        Assert.Equal(originalText, original.GetText());
        Assert.Equal(revisedText, revised.GetText());
        Assert.False(original.HasRevisions());
        Assert.False(revised.HasRevisions());
    }

    [Fact]
    public void CountsSayWhatHappened()
    {
        WordDocument original = Build("alpha beta gamma");
        WordDocument revised = Build("alpha delta gamma");

        ComparisonResult result = DocumentComparer.Compare(original, revised);

        Assert.False(result.AreIdentical);
        Assert.True(result.Insertions >= 1);
        Assert.True(result.Deletions >= 1);
    }

    /// <summary>The central contract, checked in both directions on fresh comparisons.</summary>
    private static void AssertRoundTrip(WordDocument original, WordDocument revised)
    {
        ComparisonResult accepted = DocumentComparer.Compare(original, revised);
        accepted.Document.AcceptAllRevisions();
        Assert.Equal(revised.GetText(), accepted.Document.GetText());

        ComparisonResult rejected = DocumentComparer.Compare(original, revised);
        rejected.Document.RejectAllRevisions();
        Assert.Equal(original.GetText(), rejected.Document.GetText());
    }

    private static WordDocument Build(params string[] paragraphs)
    {
        WordDocument document = WordDocument.Create();
        foreach (string text in paragraphs)
            document.Sections[0].AddParagraph(text);
        return document;
    }

    private static WordDocument WithTable(string first, string second)
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        var row = new TableRow();
        row.AddCell(first);
        row.AddCell(second);
        table.Rows.Add(row);
        document.Sections[0].Blocks.Add(table);
        return document;
    }
}
