using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Quillwright.Model;

namespace Quillwright.Rtf.Tests;

/// <summary>Opens authored annotations in Word, independently of Quillwright's own reader.</summary>
[Trait("Category", "word-oracle")]
[SupportedOSPlatform("windows")]
public class RtfWordOracleTests
{
    [Fact]
    public void AdjacentReplyAndExplicitParentForm_OpenInWord()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Reviewed text.");

        Comment adjacentRoot = document.AddComment(paragraph, 0, 8, "Adjacent root.", "Ada", "AA");
        document.AddReply(adjacentRoot, "Adjacent reply.", "Grace", "GG");
        Comment delayedRoot = document.AddComment(paragraph, 0, 8, "Delayed root.", "Linus", "LL");
        document.AddComment(paragraph, 0, 8, "Interleaved root.", "Margaret", "MM");
        document.AddReply(delayedRoot, "Delayed reply.", "Edsger", "EE");

        IReadOnlyList<WordComment> comments = OpenInWord(document);

        WordComment adjacent = Assert.Single(comments, static comment => comment.Text == "Adjacent root.");
        Assert.Contains("Adjacent reply.", adjacent.Replies);
        Assert.Contains("Delayed root.", AllTexts(comments));
        Assert.Contains("Delayed reply.", AllTexts(comments));
    }

    [Fact]
    public void FollowUpPrompt_IsNotVisibleInWord()
    {
        const string prompt = "Ask the user to add quarterly figures.";
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Reviewed text.");
        Comment followUp = document.AddComment(paragraph, 0, 8, prompt, "Ada", "AA");
        followUp.IsFollowUp = true;

        IReadOnlyList<WordComment> comments = OpenInWord(document);

        Assert.DoesNotContain(comments, comment =>
            comment.Text.Contains(prompt, StringComparison.Ordinal) ||
            comment.Replies.Any(reply => reply.Contains(prompt, StringComparison.Ordinal)));
    }

    [Fact]
    public void AnnotationTime_KeepsWordPackedDateCompatibility()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Reviewed text.");
        Comment comment = document.AddComment(paragraph, 0, 8, "Timed note.", "Ada", "AA");
        comment.Date = new DateTimeOffset(2024, 5, 6, 14, 37, 29, TimeSpan.Zero);

        WordComment reopened = Assert.Single(OpenInWord(document));

        // Word exposes its packed atndate value, whose DTTM has minute precision. The parallel
        // spec atntime retains seconds for conforming readers without changing that behaviour.
        Assert.Equal(new DateTime(2024, 5, 6, 14, 37, 0), reopened.Date);
    }

    private static IReadOnlyList<WordComment> OpenInWord(WordDocument document)
    {
        Assert.SkipUnless(
            RtfWordOracle.Enabled,
            "Set QUILLWRIGHT_WORD_ORACLE=1 and install Word to run the oracle tests.");

        string path = Path.Combine(Path.GetTempPath(), $"quillwright-rtf-oracle-{Guid.NewGuid():N}.rtf");
        File.WriteAllBytes(path, RtfWriter.Save(document).Content.ToArray());
        return (IReadOnlyList<WordComment>)RtfWordOracle.Inspect(path, ReadComments);
    }

    private static object ReadComments(object document)
    {
        object comments = RtfWordOracle.Get(document, "Comments")!;
        try
        {
            int count = Convert.ToInt32(RtfWordOracle.Get(comments, "Count"), CultureInfo.InvariantCulture);
            var result = new List<WordComment>(count);
            for (int index = 1; index <= count; index++)
            {
                object comment = RtfWordOracle.Indexed(comments, "Item", index);
                try
                {
                    string text = TextOf(comment);
                    DateTime date = Convert.ToDateTime(
                        RtfWordOracle.Get(comment, "Date"),
                        CultureInfo.InvariantCulture);
                    object replies = RtfWordOracle.Get(comment, "Replies")!;
                    try
                    {
                        int replyCount = Convert.ToInt32(
                            RtfWordOracle.Get(replies, "Count"),
                            CultureInfo.InvariantCulture);
                        var replyTexts = new List<string>(replyCount);
                        for (int replyIndex = 1; replyIndex <= replyCount; replyIndex++)
                        {
                            object reply = RtfWordOracle.Indexed(replies, "Item", replyIndex);
                            try
                            {
                                replyTexts.Add(TextOf(reply));
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(reply);
                            }
                        }

                        result.Add(new WordComment(text, date, replyTexts));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(replies);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(comment);
                }
            }

            return result;
        }
        finally
        {
            Marshal.ReleaseComObject(comments);
        }
    }

    private static string TextOf(object comment)
    {
        object range = RtfWordOracle.Get(comment, "Range")!;
        try
        {
            return ((string)RtfWordOracle.Get(range, "Text")!).TrimEnd('\r', '\a');
        }
        finally
        {
            Marshal.ReleaseComObject(range);
        }
    }

    private static IEnumerable<string> AllTexts(IEnumerable<WordComment> comments) =>
        comments.SelectMany(static comment => comment.Replies.Prepend(comment.Text));

    private sealed record WordComment(string Text, DateTime Date, IReadOnlyList<string> Replies);
}
