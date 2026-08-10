using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Rtf.Tests;

public sealed class ParagraphImportedAnchorTests
{
    [Fact]
    public void BulkImport_PreservesExistingObjectsMarksRangesAndStableOrdering()
    {
        var paragraph = new Paragraph("abcd", RunFormat.Default with { Bold = true });
        var existingBreak = new Break { Kind = BreakKind.Page };
        paragraph.InsertObject(2, existingBreak, RunFormat.Default with { Italic = true });
        var existingMark = new BookmarkStart { Id = 9, Name = "kept" };
        paragraph.AddMark(existingMark, 2);
        var existingRange = new Hyperlink { Url = "https://example.test/" };
        paragraph.AddRange(existingRange, 1, 3);
        RunFormat referenceFormat = RunFormat.Default with { StyleId = "CommentReference" };

        ImportedObjectInsertion[] objects =
        [
            new(4, 2, new CommentReference { Id = 3 }, referenceFormat),
            new(1, 1, new CommentReference { Id = 2 }, referenceFormat),
            new(1, 0, new CommentReference { Id = 1 }, referenceFormat),
        ];
        ImportedMarkPlacement[] marks =
        [
            new(4, 5, new CommentRangeEnd { Id = 3 }),
            new(2, 4, new CommentRangeEnd { Id = 2 }),
            new(1, 2, new CommentRangeStart { Id = 1 }),
            new(2, 3, new CommentRangeStart { Id = 2 }),
        ];

        paragraph.InsertImportedAnchors(objects, marks, TestContext.Current.CancellationToken);

        (int Offset, InlineObject Object)[] anchoredObjects = [.. paragraph.Objects];
        Assert.Collection(
            anchoredObjects,
            item => AssertCommentReference(item, 1, 1),
            item => AssertCommentReference(item, 2, 2),
            item =>
            {
                Assert.Equal(4, item.Offset);
                Assert.Same(existingBreak, item.Object);
            },
            item => AssertCommentReference(item, 6, 3));
        (int Offset, InlineMark Mark)[] anchoredMarks = [.. paragraph.Marks];
        Assert.Collection(
            anchoredMarks,
            item => Assert.IsType<CommentRangeStart>(AssertAt(item, 1)),
            item => Assert.IsType<CommentRangeStart>(AssertAt(item, 2)),
            item => Assert.IsType<CommentRangeEnd>(AssertAt(item, 2)),
            item => Assert.Same(existingMark, AssertAt(item, 4)),
            item => Assert.IsType<CommentRangeEnd>(AssertAt(item, 4)));
        (int Start, int Length, InlineRange Range) range = Assert.Single(paragraph.Ranges);
        Assert.Equal(1, range.Start);
        Assert.Equal(5, range.Length);
        Assert.Same(existingRange, range.Range);
        Assert.Equal("ab\ncd", paragraph.GetText());
        Assert.Equal("CommentReference", paragraph.FormatAtOffset(1).StyleId);
        Assert.True(paragraph.FormatAtOffset(3).Bold);
        Assert.True(paragraph.FormatAtOffset(4).Italic);
    }

    [Fact]
    public void BulkImport_HandlesOneHundredThousandCoincidentMidParagraphAnchors()
    {
        const int count = 100_000;
        var paragraph = new Paragraph("ab");
        RunFormat referenceFormat = RunFormat.Default with { StyleId = "CommentReference" };
        var objects = new List<ImportedObjectInsertion>(count);
        var marks = new List<ImportedMarkPlacement>(count * 2);
        for (int order = count - 1; order >= 0; order--)
        {
            int id = order + 1;
            objects.Add(new ImportedObjectInsertion(
                1,
                order,
                new CommentReference { Id = id },
                referenceFormat));
            marks.Add(new ImportedMarkPlacement(
                1,
                order * 2,
                new CommentRangeStart { Id = id }));
            marks.Add(new ImportedMarkPlacement(
                1 + order,
                (order * 2) + 1,
                new CommentRangeEnd { Id = id }));
        }

        paragraph.InsertImportedAnchors(objects, marks, TestContext.Current.CancellationToken);

        Assert.Equal(count + 2, paragraph.TextLength);
        (int Offset, InlineObject Object)[] anchoredObjects = [.. paragraph.Objects];
        Assert.Equal(count, anchoredObjects.Length);
        AssertCommentReference(anchoredObjects[0], 1, 1);
        AssertCommentReference(anchoredObjects[^1], count, count);
        (int Offset, InlineMark Mark)[] anchoredMarks = [.. paragraph.Marks];
        Assert.Equal(count * 2, anchoredMarks.Length);
        Assert.Equal(1, anchoredMarks[0].Offset);
        Assert.Equal(count, anchoredMarks[^1].Offset);
        Assert.Equal("ab", paragraph.GetText());
    }

    [Fact]
    public void BulkImport_ImportedObjectsPrecedeAnExistingObjectAtTheSameSourceOffset()
    {
        var paragraph = new Paragraph("ab");
        var existingBreak = new Break { Kind = BreakKind.Column };
        paragraph.InsertObject(1, existingBreak);
        RunFormat referenceFormat = RunFormat.Default with { StyleId = "CommentReference" };

        paragraph.InsertImportedAnchors(
        [
            new ImportedObjectInsertion(1, 1, new CommentReference { Id = 2 }, referenceFormat),
            new ImportedObjectInsertion(1, 0, new CommentReference { Id = 1 }, referenceFormat),
        ],
        [],
        TestContext.Current.CancellationToken);

        (int Offset, InlineObject Object)[] objects = [.. paragraph.Objects];
        Assert.Collection(
            objects,
            item => AssertCommentReference(item, 1, 1),
            item => AssertCommentReference(item, 2, 2),
            item =>
            {
                Assert.Equal(3, item.Offset);
                Assert.Same(existingBreak, item.Object);
            });
    }

    [Fact]
    public void BulkImport_MarksAndRangeEdgesAtAnInsertionBoundaryKeepEditingSemantics()
    {
        var paragraph = new Paragraph("abcd");
        var bookmarkStart = new BookmarkStart { Id = 7, Name = "edge" };
        var bookmarkEnd = new BookmarkEnd { Id = 7 };
        paragraph.AddMark(bookmarkStart, 2);
        paragraph.AddMark(bookmarkEnd, 2);
        var range = new Hyperlink { Url = "https://example.test/edge" };
        paragraph.AddRange(range, 2, 2);
        RunFormat referenceFormat = RunFormat.Default with { StyleId = "CommentReference" };

        paragraph.InsertImportedAnchors(
        [
            new ImportedObjectInsertion(2, 0, new CommentReference { Id = 1 }, referenceFormat),
            new ImportedObjectInsertion(4, 1, new CommentReference { Id = 2 }, referenceFormat),
        ],
        [],
        TestContext.Current.CancellationToken);

        Assert.Collection(
            paragraph.Marks,
            item => Assert.Same(bookmarkStart, AssertAt(item, 2)),
            item => Assert.Same(bookmarkEnd, AssertAt(item, 2)));
        (int Start, int Length, InlineRange Range) anchoredRange = Assert.Single(paragraph.Ranges);
        Assert.Equal(2, anchoredRange.Start);
        Assert.Equal(3, anchoredRange.Length);
        Assert.Same(range, anchoredRange.Range);
    }

    [Fact]
    public void BulkImport_PreservesSurroundingRunKindsAndAttributes()
    {
        var paragraph = new Paragraph();
        RunFormat fieldFormat = RunFormat.Default with { Bold = true };
        RunFormat deletedFormat = RunFormat.Default with { Italic = true };
        paragraph.AppendRunText("ab", fieldFormat, RunKind.FieldInstruction, " field='one'");
        paragraph.AppendRunText("cd", deletedFormat, RunKind.Deleted, " deleted='two'");
        RunFormat referenceFormat = RunFormat.Default with { StyleId = "CommentReference" };

        paragraph.InsertImportedAnchors(
        [
            new ImportedObjectInsertion(1, 0, new CommentReference { Id = 1 }, referenceFormat),
            new ImportedObjectInsertion(2, 1, new CommentReference { Id = 2 }, referenceFormat),
            new ImportedObjectInsertion(
                4,
                2,
                new CommentReference { Id = 3 },
                referenceFormat,
                UseAppendRunSemantics: true),
        ],
        [],
        TestContext.Current.CancellationToken);

        AssertRun(paragraph, 1, RunKind.FieldInstruction, " field='one'");
        AssertRun(paragraph, 3, RunKind.Deleted, " deleted='two'");
        AssertRun(paragraph, 6, RunKind.Text, null);
        AssertRun(paragraph, 2, RunKind.FieldInstruction, " field='one'");
        AssertRun(paragraph, 4, RunKind.Deleted, " deleted='two'");
    }

    [Fact]
    public void BulkImport_MarksOnlyLeavesTextRunsObjectsAndRangesUnchanged()
    {
        var paragraph = new Paragraph("ab", RunFormat.Default with { Bold = true });
        var existingObject = new Break { Kind = BreakKind.Line };
        paragraph.InsertObject(1, existingObject);
        var existingMark = new BookmarkStart { Id = 3, Name = "existing" };
        paragraph.AddMark(existingMark, 1);
        var existingRange = new Hyperlink { Anchor = "target" };
        paragraph.AddRange(existingRange, 0, paragraph.TextLength);

        paragraph.InsertImportedAnchors(
            [],
        [
            new ImportedMarkPlacement(2, 1, new CommentRangeEnd { Id = 1 }),
            new ImportedMarkPlacement(1, 0, new CommentRangeStart { Id = 1 }),
        ],
            TestContext.Current.CancellationToken);

        Assert.Equal(3, paragraph.TextLength);
        Assert.Same(existingObject, Assert.Single(paragraph.Objects).Object);
        Assert.Collection(
            paragraph.Marks,
            item => Assert.Same(existingMark, AssertAt(item, 1)),
            item => Assert.IsType<CommentRangeStart>(AssertAt(item, 1)),
            item => Assert.IsType<CommentRangeEnd>(AssertAt(item, 2)));
        (int Start, int Length, InlineRange Range) range = Assert.Single(paragraph.Ranges);
        Assert.Equal(0, range.Start);
        Assert.Equal(3, range.Length);
        Assert.Same(existingRange, range.Range);
        Assert.True(paragraph.FormatAtOffset(0).Bold);
    }

    private static void AssertCommentReference(
        (int Offset, InlineObject Object) item,
        int expectedOffset,
        int expectedId)
    {
        Assert.Equal(expectedOffset, item.Offset);
        Assert.Equal(expectedId, Assert.IsType<CommentReference>(item.Object).Id);
    }

    private static InlineMark AssertAt((int Offset, InlineMark Mark) item, int expectedOffset)
    {
        Assert.Equal(expectedOffset, item.Offset);
        return item.Mark;
    }

    private static void AssertRun(
        Paragraph paragraph,
        int offset,
        RunKind expectedKind,
        string? expectedAttributes)
    {
        RunSpan run = paragraph.RunSpans.ToArray().Single(candidate =>
            candidate.Start <= offset && offset < candidate.End);
        Assert.Equal(expectedKind, run.Kind);
        Assert.Equal(expectedAttributes, run.Attributes);
    }
}
