using Inkwright;
using Inkwright.Annotations;
using Inkwright.Cos;
using Inkwright.Layout;
using Inkwright.Tagging;
using Quillwright.Model;
using Xunit;

namespace Quillwright.Pdf.Tests;

public class CommentTests
{
    [Fact]
    public void Comments_AreSkippedAndReportedOnce()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Reviewed text.");
        Comment comment = document.AddComment(paragraph, 0, 8, "First review note.");
        document.AddReply(comment, "A reply.");

        using Rendered rendered = Rendered.Of(document);

        PdfExportWarning warning = Assert.Single(rendered.Diagnostics, static candidate =>
            candidate.Kind == PdfExportWarningKind.ContentSkipped && candidate.Subject == "comments");
        Assert.Contains("review state", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("First review note", rendered.Text(), StringComparison.Ordinal);
        Assert.DoesNotContain("A reply", rendered.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void Comments_BecomeInteractivePdfThreadsWhenRequested()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Reviewed text.");
        Comment root = document.AddComment(paragraph, 0, 8, "First review note.", "Ada", "A");
        root.DateUtc = new DateTimeOffset(2026, 8, 8, 10, 15, 0, TimeSpan.Zero);
        root.DurableId = "0123456789ABCDEF0123456789ABCDEF";
        Comment reply = document.AddReply(root, "A reply.", "Grace", "G");
        reply.DateUtc = new DateTimeOffset(2026, 8, 8, 11, 30, 0, TimeSpan.Zero);

        using Rendered rendered = Rendered.Of(
            document,
            new PdfExportOptions { IncludeComments = true });

        PdfTextAnnotation note = Assert.IsType<PdfTextAnnotation>(
            Assert.Single(rendered.Document.Pages[0].Annotations.Comments));
        Assert.Equal("First review note.", note.Contents);
        Assert.Equal("Ada", note.Author);
        Assert.Equal(root.DateUtc, note.CreatedAt);
        Assert.Equal(root.DurableId, note.Name);
        Assert.Equal(PdfReviewState.None, note.ReviewState);
        Assert.Contains(note.Replies, candidate =>
            candidate.Contents == "A reply." && candidate.Author == "Grace");
        Assert.DoesNotContain(rendered.Diagnostics, warning => warning.Subject == "comments");
    }

    [Fact]
    public void DeepCommentChainRendersWithoutRecursiveStackGrowth()
    {
        const int MessageCount = 10_000;

        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Reviewed text.");
        Comment current = document.AddComment(paragraph, 0, 8, "Root", "Author 1", "A1");

        for (int id = 2; id <= MessageCount; id++)
            current = AddDetachedReply(document, current, id, $"Reply {id}");

        using PdfDocument pdf = PdfExporter.Render(
            document,
            new PdfExportOptions { IncludeComments = true }).Document;

        PdfMarkupAnnotation[] messages =
            [.. pdf.Pages[0].Annotations.OfType<PdfMarkupAnnotation>()];
        Assert.Equal(MessageCount, messages.Length);
        Assert.Equal($"Reply {MessageCount}", messages[^1].Contents);
        Assert.Equal(messages[^2].Name, messages[^1].InReplyTo?.Name);
    }

    [Fact]
    public void SiblingRepliesKeepSourceOrderAndParentageAfterSaveReload()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Reviewed text.");
        Comment root = document.AddComment(paragraph, 0, 8, "Root", "Ada", "A");
        root.DurableId = "ROOT";
        Comment first = document.AddReply(root, "First", "Grace", "G");
        first.DurableId = "FIRST";
        Comment nested = document.AddReply(first, "Nested", "Linus", "L");
        nested.DurableId = "NESTED";
        Comment second = document.AddReply(root, "Second", "Margaret", "M");
        second.DurableId = "SECOND";
        Comment third = document.AddReply(root, "Third", "Edsger", "E");
        third.DurableId = "THIRD";

        using Rendered rendered = Rendered.Of(
            document,
            new PdfExportOptions { IncludeComments = true });

        PdfMarkupAnnotation[] messages =
            [.. rendered.Document.Pages[0].Annotations.OfType<PdfMarkupAnnotation>()];
        Assert.Equal(["ROOT", "FIRST", "NESTED", "SECOND", "THIRD"],
            messages.Select(static message => message.Name));
        Assert.Equal("ROOT", messages[1].InReplyTo?.Name);
        Assert.Equal("FIRST", messages[2].InReplyTo?.Name);
        Assert.Equal("ROOT", messages[3].InReplyTo?.Name);
        Assert.Equal("ROOT", messages[4].InReplyTo?.Name);
    }

    [Fact]
    public void InteractiveCommentsDoNotChangeTextLayout()
    {
        WordDocument document = WordDocument.Create();
        string text = string.Join(' ', Enumerable.Repeat(
            "A review marker beside wrapped text must occupy no layout width.", 8));
        Paragraph paragraph = document.Sections[0].AddParagraph(text);
        document.AddComment(paragraph, 52, 86, "Check the wrapped sentence.", "Ada", "A");

        using Rendered withoutComments = Rendered.Of(document);
        using Rendered withComments = Rendered.Of(
            document,
            new PdfExportOptions { IncludeComments = true });

        Assert.Equal(withoutComments.PageCount, withComments.PageCount);
        Assert.Equal(LayoutOf(withoutComments), LayoutOf(withComments));

        static (string Text, double X, double Y, double Width)[] LayoutOf(Rendered rendered) =>
            [.. rendered.Letters().Select(letter => (
                letter.Text,
                Math.Round(letter.Origin.X, 4),
                Math.Round(letter.Origin.Y, 4),
                Math.Round(letter.Width, 4)))];
    }

    [Fact]
    public void ResolvedMessagesBecomeInformationalRepliesWithoutInventedProvenance()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Reviewed text.");
        Comment root = document.AddComment(paragraph, 0, 8, "A root note.", "Ada", "A");
        root.IsResolved = true;
        Comment reply = document.AddReply(root, "Resolved reply.", "Grace", "G");
        reply.IsResolved = true;

        using Rendered rendered = Rendered.Of(
            document,
            new PdfExportOptions { IncludeComments = true });

        PdfTextAnnotation note = Assert.IsType<PdfTextAnnotation>(
            Assert.Single(rendered.Document.Pages[0].Annotations.Comments));
        PdfMarkupAnnotation pdfReply = Assert.Single(note.Replies, candidate =>
            candidate.Contents == "Resolved reply.");
        PdfMarkupAnnotation rootInformation = Assert.Single(note.Replies, candidate =>
            candidate.Contents == _resolutionInformation);
        PdfMarkupAnnotation replyInformation = Assert.Single(pdfReply.Replies);

        Assert.Equal(PdfReviewState.None, note.ReviewState);
        Assert.Equal(PdfReviewState.None, pdfReply.ReviewState);
        AssertInformationalReply(rootInformation);
        AssertInformationalReply(replyInformation);

        PdfExportWarning warning = Assert.Single(rendered.Diagnostics, static candidate =>
            candidate.Kind == PdfExportWarningKind.ContentSkipped &&
            candidate.Subject == "comment-resolved-state");
        Assert.Contains("2 resolved", warning.Message, StringComparison.Ordinal);
        Assert.Contains("/State /T", warning.Message, StringComparison.Ordinal);
        Assert.Contains("informational reply", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FollowUpPromptIsNotExposedAsCommentBody()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Reviewed text.");
        Comment followUp = document.AddComment(
            paragraph,
            0,
            8,
            "This producer prompt should be ignored.",
            "Ada",
            "A");
        followUp.IsFollowUp = true;

        using Rendered rendered = Rendered.Of(
            document,
            new PdfExportOptions { IncludeComments = true });

        PdfTextAnnotation note = Assert.IsType<PdfTextAnnotation>(
            Assert.Single(rendered.Document.Pages[0].Annotations.Comments));
        Assert.Equal("Follow-up", note.Contents);
        Assert.Equal("Word follow-up", note.Subject);
    }

    [Fact]
    public void MixedDirectionCommentUsesTheLogicalEndOfTheRtlRange()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("LTR אבג end");
        document.AddComment(paragraph, 4, 3, "RTL endpoint.", "Ada", "A");

        using Rendered rendered = Rendered.Of(
            document,
            new PdfExportOptions { IncludeComments = true });

        PdfTextAnnotation note = Assert.IsType<PdfTextAnnotation>(
            Assert.Single(rendered.Document.Pages[0].Annotations.Comments));
        var letters = rendered.Letters();
        double hebrewLeft = letters
            .Where(letter => letter.Text.Any(character => character is >= '\u0590' and <= '\u05FF'))
            .Min(letter => letter.Origin.X);

        Assert.True(
            Math.Abs(hebrewLeft - note.Rectangle.Left) < 0.001,
            $"note={note.Rectangle.Left}; letters={string.Join(", ", letters.Select(letter => $"{letter.Text}@{letter.Origin.X:F3}"))}");
    }

    [Fact]
    public void DanglingCommentReferenceIsDiagnosed()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Text");
        paragraph.AppendObject(new CommentReference { Id = 404 });

        using Rendered rendered = Rendered.Of(
            document,
            new PdfExportOptions { IncludeComments = true });

        PdfExportWarning warning = Assert.Single(rendered.Diagnostics, static candidate =>
            candidate.Subject == "comment-references");
        Assert.Contains("404", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TaggedInteractiveCommentsHaveBidirectionalAnnotationStructure()
    {
        WordDocument document = WordDocument.Create();
        document.Properties.Title = "Accessible review";
        Paragraph paragraph = document.Sections[0].AddParagraph("Reviewed text.");
        Comment root = document.AddComment(paragraph, 0, 8, "Check this wording.", "Ada", "A");
        root.IsResolved = true;
        document.AddReply(root, "Agreed.", "Grace", "G");

        var options = new PdfExportOptions
        {
            Tagged = true,
            Language = "en-GB",
            IncludeComments = true,
        };

        using Rendered rendered = Rendered.Of(
            document,
            options,
            pdf => PdfUaProfile.Declare(pdf, PdfUaConformance.Ua1, "Accessible review"));

        IReadOnlyList<PdfUaProblem> problems =
            PdfUaProfile.Validate(rendered.Document, PdfUaConformance.Ua1);
        Assert.Empty(PdfUaProfile.Violations(problems));

        PdfAnnotation[] interactive = [.. rendered.Document.Pages[0].Annotations
            .Where(annotation => annotation.Type != PdfAnnotationType.Popup)];
        Assert.Equal(3, interactive.Length);
        Assert.All(interactive, annotation =>
            Assert.Equal(
                Inkwright.Cos.PdfValueKind.Integer,
                annotation.Dictionary.Get(Inkwright.Cos.PdfName.Get("StructParent")).Kind));

        PdfStructureNode owner = rendered.Document.Structure
            .SelectMany(static rootNode => rootNode.Descendants())
            .Single(static node => node.Tag == "P");
        int firstContent = owner.Kids.ToList().FindIndex(static kid => kid is PdfStructureContentKid);
        int firstAnnotation = owner.Kids.ToList().FindIndex(static kid =>
            kid is PdfStructureElementKid { Element.Tag: "Annot" });
        int lastContent = owner.Kids.ToList().FindLastIndex(static kid => kid is PdfStructureContentKid);

        Assert.True(firstContent >= 0 && firstContent < firstAnnotation);
        Assert.True(firstAnnotation < lastContent);
    }

    private const string _resolutionInformation =
        "Resolved in the source Word document; resolver identity and time are unavailable.";

    private static void AssertInformationalReply(PdfMarkupAnnotation reply)
    {
        Assert.Equal(_resolutionInformation, reply.Contents);
        Assert.True(reply.IsReply);
        Assert.Equal(PdfReviewState.None, reply.ReviewState);
        Assert.Null(reply.Author);
        Assert.Null(reply.CreatedAt);
        Assert.Null(reply.ModifiedAt);
        Assert.False(reply.Dictionary.ContainsKey(PdfName.Get("T")));
        Assert.False(reply.Dictionary.ContainsKey(PdfName.Get("State")));
        Assert.False(reply.Dictionary.ContainsKey(PdfName.Get("StateModel")));
    }

    private static Comment AddDetachedReply(
        WordDocument document,
        Comment parent,
        int id,
        string text)
    {
        var reply = new Comment(document)
        {
            Id = id,
            ParentId = parent.Id,
            Author = $"Author {id}",
        };

        reply.AddParagraph(text);
        document.CommentList.Add(reply);
        return reply;
    }
}
