using Quillwright.Editing;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Tests;

/// <summary>
/// Recording edits as tracked changes (ISO/IEC 29500-1 §17.13.5): an insertion is wrapped, a
/// deletion stays where it is under a mark, and accepting or rejecting afterwards produces
/// the two documents the author was choosing between.
/// </summary>
public class RevisionRecordingTests
{
    private static readonly DateTimeOffset When = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InsertedText_IsWrappedInAnInsertion()
    {
        WordDocument document = Document("Hello world");
        Paragraph paragraph = First(document);

        using (document.TrackChanges("Ada Lovelace", When))
            paragraph.InsertText(5, " brave");

        Revision revision = Revisions(paragraph).Single();
        Assert.Equal(RevisionKind.Inserted, revision.Kind);
        Assert.Equal("Ada Lovelace", revision.Author);
        Assert.Equal(When, revision.Date);
        Assert.Equal("Hello brave world", paragraph.Text);
    }

    [Fact]
    public void DeletedText_StaysWhereItIsUnderAMark()
    {
        WordDocument document = Document("Hello brave world");
        Paragraph paragraph = First(document);

        using (document.TrackChanges("Ada", When))
            paragraph.RemoveText(5, 6);

        Assert.Equal(RevisionKind.Deleted, Revisions(paragraph).Single().Kind);
        Assert.Equal("Hello brave world", paragraph.Text);
    }

    [Fact]
    public async Task DeletedText_IsWrittenAsRemovedText()
    {
        WordDocument document = Document("Hello brave world");

        using (document.TrackChanges("Ada", When))
            First(document).RemoveText(5, 6);

        string markup = await MarkupAsync(document);

        Assert.Contains("<w:del ", markup, StringComparison.Ordinal);
        Assert.Contains("<w:delText xml:space=\"preserve\"> brave</w:delText>", markup, StringComparison.Ordinal);
    }

    /// <summary>A replacement is the deletion first and the insertion after it, as Word writes it.</summary>
    [Fact]
    public async Task AReplacement_RecordsBothHalvesInOrder()
    {
        WordDocument document = Document("the draft report");

        using (document.TrackChanges("Ada", When))
            document.Replace("draft", "final");

        Paragraph paragraph = First(document);
        Assert.Equal(["Deleted", "Inserted"], Revisions(paragraph).Select(static r => r.Kind.ToString()));
        Assert.Equal("the draftfinal report", paragraph.Text);

        string markup = await MarkupAsync(document);
        Assert.InRange(
            markup.IndexOf("<w:del ", StringComparison.Ordinal),
            0,
            markup.IndexOf("<w:ins ", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptingARecordedChange_LeavesTheNewText()
    {
        WordDocument document = Document("the draft report");
        using (document.TrackChanges("Ada", When))
            document.Replace("draft", "final");

        document.AcceptAllRevisions();

        Assert.Equal("the final report", First(document).Text);
        Assert.False(document.HasRevisions());
    }

    [Fact]
    public void RejectingARecordedChange_LeavesTheOldText()
    {
        WordDocument document = Document("the draft report");
        using (document.TrackChanges("Ada", When))
            document.Replace("draft", "final");

        document.RejectAllRevisions();

        Assert.Equal("the draft report", First(document).Text);
        Assert.False(document.HasRevisions());
    }

    /// <summary>
    /// Rejecting a deletion has to untag the text as well as drop the mark, or the runs still
    /// read as removed and the package will not open.
    /// </summary>
    [Fact]
    public async Task RejectingADeletion_LeavesAValidPackage()
    {
        WordDocument document = Document("Hello brave world");
        using (document.TrackChanges("Ada", When))
            First(document).RemoveText(5, 6);

        document.RejectAllRevisions();
        string markup = await MarkupAsync(document);

        Assert.DoesNotContain("delText", markup, StringComparison.Ordinal);
        Assert.Equal("Hello brave world", First(document).Text);
    }

    /// <summary>Undoing your own insertion should leave no trace of either half.</summary>
    [Fact]
    public void DeletingTextThisSessionInserted_RemovesItOutright()
    {
        WordDocument document = Document("Hello world");
        Paragraph paragraph = First(document);

        using (document.TrackChanges("Ada", When))
        {
            paragraph.InsertText(5, " brave");
            paragraph.RemoveText(5, 6);
        }

        Assert.Equal("Hello world", paragraph.Text);
        Assert.Empty(Revisions(paragraph));
    }

    [Fact]
    public void TypingOneLetterAtATime_IsOneInsertion()
    {
        WordDocument document = Document("ab");
        Paragraph paragraph = First(document);

        using (document.TrackChanges("Ada", When))
        {
            for (int i = 0; i < 4; i++)
                paragraph.InsertText(1 + i, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Single(Revisions(paragraph));
        Assert.Equal("a0123b", paragraph.Text);
    }

    [Fact]
    public void AParagraphAddedWhileRecording_HasAnInsertedMark()
    {
        WordDocument document = Document("first");

        using (document.TrackChanges("Ada", When))
            document.Sections[0].AddParagraph("second");

        Paragraph added = document.Sections[0].Blocks.Paragraphs.Last();
        Assert.Contains("<w:ins ", added.MarkFormat.MarkRevisionXml!, StringComparison.Ordinal);
        Assert.Equal(RevisionKind.Inserted, Revisions(added).Single().Kind);
    }

    [Fact]
    public void DeletingAParagraphWhileRecording_LeavesItMarked()
    {
        WordDocument document = Document("first");
        document.Sections[0].AddParagraph("second");

        using (document.TrackChanges("Ada", When))
            document.Sections[0].Blocks.Paragraphs.Last().Delete();

        Paragraph marked = document.Sections[0].Blocks.Paragraphs.Last();
        Assert.Equal(2, document.Sections[0].Blocks.Count);
        Assert.Contains("<w:del ", marked.MarkFormat.MarkRevisionXml!, StringComparison.Ordinal);
        Assert.Equal(RevisionKind.Deleted, Revisions(marked).Single().Kind);
    }

    [Fact]
    public void AcceptingADeletedParagraph_MergesItIntoTheNextOne()
    {
        WordDocument document = Document("first");
        document.Sections[0].AddParagraph("second");

        using (document.TrackChanges("Ada", When))
            document.Sections[0].Blocks.Paragraphs.First().Delete();

        document.AcceptAllRevisions();

        Assert.Equal("second", Assert.Single(document.Sections[0].Blocks.Paragraphs).Text);
    }

    [Fact]
    public void DeletingAParagraphThisSessionAdded_RemovesItOutright()
    {
        WordDocument document = Document("first");

        using (document.TrackChanges("Ada", When))
        {
            Paragraph added = document.Sections[0].AddParagraph("second");
            added.Delete();
        }

        Assert.Single(document.Sections[0].Blocks);
        Assert.False(document.HasRevisions());
    }

    [Fact]
    public async Task AFormattingChange_RecordsWhatTheFormattingWas()
    {
        WordDocument document = Document("plain text");

        using (document.TrackChanges("Ada", When))
            First(document).ApplyFormat(0, 5, static f => f with { Bold = true });

        string markup = await MarkupAsync(document);

        Assert.Contains("<w:rPrChange ", markup, StringComparison.Ordinal);
        Assert.Contains("w:author=\"Ada\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AFormattingChangeThatChangesNothing_IsNotRecorded()
    {
        WordDocument document = Document("plain text");

        using (document.TrackChanges("Ada", When))
            First(document).ApplyFormat(0, 5, static f => f);

        Assert.All(First(document).Runs, static run => Assert.Null(run.Format.ChangeXml));
    }

    /// <summary>
    /// Inserting into text already marked deleted has to break the deletion in two, or the
    /// new text ends up inside a <c>w:del</c> claiming to be present.
    /// </summary>
    [Fact]
    public async Task InsertingInsideADeletion_SplitsIt()
    {
        WordDocument document = Document("abcdef");
        Paragraph paragraph = First(document);

        using (document.TrackChanges("Ada", When))
        {
            paragraph.RemoveText(0, 6);
            paragraph.InsertText(3, "XY");
        }

        List<Revision> revisions = Revisions(paragraph);
        Assert.Equal(2, revisions.Count(static r => r.Kind == RevisionKind.Deleted));
        Assert.Single(revisions, static r => r.Kind == RevisionKind.Inserted);
        Assert.Distinct(revisions.Select(static r => r.Id));

        await MarkupAsync(document);
    }

    [Fact]
    public async Task RecordedChangesSurviveARoundTrip()
    {
        WordDocument document = Document("the draft report");
        using (document.TrackChanges("Ada Lovelace", When))
            document.Replace("draft", "final");

        WordDocument reopened = await DocumentFixture.RoundTripAsync(document, "recorded changes");

        Assert.True(reopened.HasRevisions());
        reopened.AcceptAllRevisions();
        Assert.Equal("the final report", reopened.Sections[0].Blocks.Paragraphs.First().Text);
    }

    [Fact]
    public void TheSession_PutsTheDocumentsOwnSettingBack()
    {
        WordDocument document = Document("text");

        using (document.TrackChanges("Ada", When))
            Assert.True(document.Settings.TrackRevisions);

        Assert.False(document.Settings.TrackRevisions);
        Assert.Null(document.ActiveTracking);
    }

    [Fact]
    public void TwoSessionsAtOnce_AreRefused()
    {
        WordDocument document = Document("text");

        using (document.TrackChanges("Ada", When))
            Assert.Throws<InvalidOperationException>(() => document.TrackChanges("Grace", When));
    }

    /// <summary>Identifiers have to clear the ones an earlier author already used.</summary>
    [Fact]
    public void RecordedChanges_DoNotReuseAnExistingIdentifier()
    {
        WordDocument document = Document("existing text");
        Paragraph paragraph = First(document);
        paragraph.AddRange(new Revision { Kind = RevisionKind.Inserted, Id = 77, Author = "Grace" }, 0, 8);

        using (document.TrackChanges("Ada", When))
            paragraph.InsertText(paragraph.TextLength, " more");

        Assert.Contains(Revisions(paragraph), static r => r.Author == "Ada" && r.Id > 77);
    }

    [Fact]
    public void EditingWithNoSession_ChangesTheTextOutright()
    {
        WordDocument document = Document("the draft report");

        document.Replace("draft", "final");

        Assert.Equal("the final report", First(document).Text);
        Assert.False(document.HasRevisions());
    }

    private static WordDocument Document(string text)
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph(text);
        return document;
    }

    private static Paragraph First(WordDocument document) => document.Sections[0].Blocks.Paragraphs.First();

    private static List<Revision> Revisions(Paragraph paragraph) =>
    [
        .. paragraph.Ranges.OrderBy(static entry => entry.Start).Select(static entry => entry.Range).OfType<Revision>(),
    ];

    private static async Task<string> MarkupAsync(WordDocument document)
    {
        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
        OpenXmlAssert.Valid(buffer, "recorded changes");
        return OpenXmlAssert.ReadPart(buffer, "document.xml");
    }
}
