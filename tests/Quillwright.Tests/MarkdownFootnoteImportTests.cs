using System.Text;
using Quillwright.Diagnostics;
using Quillwright.Markdown;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Tests;

public sealed class MarkdownFootnoteImportTests
{
    [Fact]
    public void ExportedNotes_RoundTripWithRepeatedReferencesAndMultipleParagraphs()
    {
        WordDocument source = WordDocument.Create();
        Paragraph body = source.Sections[0].AddParagraph("Body");
        Note footnote = source.AddFootnote(body, "First paragraph.");
        footnote.AddParagraph("Second **literal** paragraph.");
        body.AppendText(" repeated", RunFormat.Default);
        body.AppendObject(new NoteReference { Id = footnote.Id });
        source.AddEndnote(body, "Endnote text.");

        string markdown = source.ToMarkdown().Text;
        MarkdownImportResult imported = MarkdownImporter.Import(markdown);

        Assert.True(imported.Diagnostics.IsEmpty, markdown + Environment.NewLine + imported.Diagnostics);
        Note importedFootnote = Assert.Single(imported.Document.Footnotes, static n => n.Kind == NoteKind.Normal);
        Note importedEndnote = Assert.Single(imported.Document.Endnotes, static n => n.Kind == NoteKind.Normal);
        Assert.Equal(footnote.Id, importedFootnote.Id);
        Assert.Equal("First paragraph.", importedFootnote.Blocks.Paragraphs.ElementAt(0).GetText());
        Assert.Equal("Second **literal** paragraph.", importedFootnote.Blocks.Paragraphs.ElementAt(1).GetText());
        Assert.Equal("Endnote text.", importedEndnote.Blocks.Paragraphs.Single().GetText());

        List<NoteReference> references =
        [
            .. imported.Document.Sections[0].Blocks.Paragraphs.Single().Objects
                .Select(static item => item.Object)
                .OfType<NoteReference>(),
        ];
        Assert.Equal(3, references.Count);
        Assert.Equal(references[0].Id, references[1].Id);
        Assert.False(references[0].IsEndnote);
        Assert.True(references[2].IsEndnote);
        Assert.Equal(markdown, imported.Document.ToMarkdown().Text);
    }

    [Fact]
    public void ExporterLabels_RestoreIdsAndFirstReferenceOrder()
    {
        const string Markdown =
            "Body[^fn-7] then[^fn-2] and again[^fn-7].\n\n" +
            "[^fn-2]: second definition\n\n" +
            "[^fn-7]: first line\n" +
            "    continued line\n" +
            "    \n" +
            "    second paragraph\n";

        MarkdownImportResult result = MarkdownImporter.Import(Markdown);

        Assert.True(result.Diagnostics.IsEmpty, result.Diagnostics.ToString());
        List<Note> notes = [.. result.Document.Footnotes.Where(static note => note.Kind == NoteKind.Normal)];
        Assert.Equal([7, 2], notes.Select(static note => note.Id));
        Assert.Equal("first line continued line", notes[0].Blocks.Paragraphs.ElementAt(0).GetText());
        Assert.Equal("second paragraph", notes[0].Blocks.Paragraphs.ElementAt(1).GetText());
        Assert.Equal("second definition", notes[1].Blocks.Paragraphs.Single().GetText());
    }

    [Fact]
    public void AnArbitraryEnLabel_RemainsAnOrdinaryFootnote()
    {
        WordDocument document = MarkdownImporter.Import(
            "Body[^en-topic].\n\n[^en-topic]: ordinary footnote").Document;

        Assert.Single(document.Footnotes, static note => note.Kind == NoteKind.Normal);
        Assert.Empty(document.Endnotes);
    }

    [Fact]
    public async Task ImportedNotes_SaveAndReloadAsValidDocxParts()
    {
        WordDocument imported = MarkdownImporter.Import(
            "Body[^fn-9] and end[^en-4].\n\n" +
            "[^fn-9]: footnote body\n\n" +
            "[^en-4]: endnote body").Document;

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(imported, "Markdown footnotes");

        Note footnote = Assert.Single(reloaded.Footnotes, static note => note.Kind == NoteKind.Normal);
        Note endnote = Assert.Single(reloaded.Endnotes, static note => note.Kind == NoteKind.Normal);
        Assert.Equal(9, footnote.Id);
        Assert.Equal(4, endnote.Id);
        Assert.Equal("footnote body", footnote.Blocks.Paragraphs.Single().GetText());
        Assert.Equal("endnote body", endnote.Blocks.Paragraphs.Single().GetText());
    }

    [Fact]
    public void MalformedDuplicateAndDanglingFootnotes_AreKeptOrDiagnosed()
    {
        const string Markdown =
            "Valid[^dup] missing[^missing] malformed[^].\n\n" +
            "[^dup]: first\n" +
            "[^DUP]: second\n" +
            "[^orphan]: unused\n";

        MarkdownImportResult result = MarkdownImporter.Import(Markdown);

        Assert.Contains("missing[^missing] malformed[^]", result.Document.GetText(), StringComparison.Ordinal);
        Assert.Single(result.Document.Footnotes, static note => note.Kind == NoteKind.Normal);
        Assert.Contains(result.Diagnostics, static warning =>
            warning.Kind == MarkdownImportWarningKind.FootnoteMalformed && warning.Subject == "DUP");
        Assert.Contains(result.Diagnostics, static warning =>
            warning.Kind == MarkdownImportWarningKind.FootnoteMalformed && warning.Subject == "[^]");
        Assert.Contains(result.Diagnostics, static warning =>
            warning.Kind == MarkdownImportWarningKind.FootnoteDangling && warning.Subject == "missing");
        Assert.Contains(result.Diagnostics, static warning =>
            warning.Kind == MarkdownImportWarningKind.FootnoteDangling && warning.Subject == "orphan");
    }

    [Fact]
    public void AShorterFenceInsideCode_DoesNotExposeAFootnoteDefinition()
    {
        const string Markdown =
            "````\n```\n[^fn-1]: code, not a definition\n```\n````\n\nBody[^fn-1]";

        MarkdownImportResult result = MarkdownImporter.Import(Markdown);

        Assert.Empty(result.Document.Footnotes);
        Assert.Contains("[^fn-1]: code, not a definition", result.Document.GetText(), StringComparison.Ordinal);
        Assert.Contains("Body[^fn-1]", result.Document.GetText(), StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, static warning =>
            warning.Kind == MarkdownImportWarningKind.FootnoteDangling && warning.Subject == "fn-1");
    }

    [Fact]
    public void NoteReferenceChains_AreExpandedIteratively()
    {
        const int Count = 1_000;
        var markdown = new StringBuilder("Body[^fn-1]\n\n");
        for (int id = 1; id <= Count; id++)
        {
            markdown.Append("[^fn-").Append(id).Append("]: note ").Append(id);
            if (id < Count)
                markdown.Append("[^fn-").Append(id + 1).Append(']');
            markdown.Append("\n\n");
        }

        MarkdownImportResult result = MarkdownImporter.Import(markdown.ToString());

        Assert.Equal(Count, result.Document.Footnotes.Count(static note => note.Kind == NoteKind.Normal));
        Assert.True(result.Diagnostics.IsEmpty, result.Diagnostics.ToString());
    }

    [Fact]
    public void ManyNumericNoteLabels_KeepSeparateLinearIdStateAndReuseRepeatedLabels()
    {
        const int Count = 10_000;
        var markdown = new StringBuilder("Body ");
        for (int id = 1; id <= Count; id++)
            markdown.Append("[^fn-").Append(id).Append(']');
        markdown.Append(" repeat[^fn-1] end[^en-1]\n\n");
        for (int id = 1; id <= Count; id++)
            markdown.Append("[^fn-").Append(id).Append("]: note ").Append(id).Append('\n');
        markdown.Append("[^en-1]: endnote\n");

        MarkdownImportResult result = MarkdownImporter.Import(markdown.ToString());

        Note[] footnotes =
        [.. result.Document.Footnotes.Where(static note => note.Kind == NoteKind.Normal)];
        Note endnote = Assert.Single(result.Document.Endnotes, static note => note.Kind == NoteKind.Normal);
        Assert.Equal(Count, footnotes.Length);
        Assert.Equal(Enumerable.Range(1, Count), footnotes.Select(static note => note.Id));
        Assert.Equal(1, endnote.Id);

        NoteReference[] references =
        [
            .. result.Document.Sections[0].Blocks.Paragraphs.Single().Objects
                .Select(static item => item.Object)
                .OfType<NoteReference>(),
        ];
        Assert.Equal(Count + 2, references.Length);
        Assert.Equal(references[0].Id, references[Count].Id);
        Assert.False(references[Count].IsEndnote);
        Assert.True(references[^1].IsEndnote);
        Assert.True(result.Diagnostics.IsEmpty, result.Diagnostics.ToString());
    }

    [Fact]
    public void PreferredIdsAdvanceFallbackStateAndWrapAfterTheMaximum()
    {
        const string Markdown =
            "Body[^fn-7][^custom][^fn-2147483646][^fn-2147483647][^after]\n\n" +
            "[^fn-7]: seven\n" +
            "[^custom]: next\n" +
            "[^fn-2147483646]: penultimate\n" +
            "[^fn-2147483647]: maximum\n" +
            "[^after]: wrapped\n";

        MarkdownImportResult result = MarkdownImporter.Import(Markdown);

        Assert.Equal(
            [7, 8, int.MaxValue - 1, int.MaxValue, 1],
            result.Document.Footnotes
                .Where(static note => note.Kind == NoteKind.Normal)
                .Select(static note => note.Id));
        Assert.True(result.Diagnostics.IsEmpty, result.Diagnostics.ToString());
    }

    [Fact]
    public async Task MalformedAutolinksAndUnmatchedEmphasis_ObserveCancellation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        string markdown = string.Concat(Enumerable.Repeat("<a a* ", 2_000_000));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task parse = Task.Run(
            () =>
            {
                started.SetResult();
                var parser = new MarkdownInlineParser(
                    new Dictionary<string, (string Url, string? Title)>(),
                    static (_, _, _) => null,
                    static (_, _, _, _) => false,
                    static (_, _) => { },
                    new DocumentLoadBudgetState(DocumentLoadBudget.Default),
                    cancellation.Token);
                parser.Fill(new Paragraph(), markdown, RunFormat.Default, 1);
            },
            TestContext.Current.CancellationToken);

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await Task.Delay(1, TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parse);
    }

    [Fact]
    public void NoteDefinitionsShareTheMarkupNodeBudget()
    {
        const string Markdown = "[^one]: first\n[^two]: second\n";
        var options = new MarkdownImportOptions
        {
            Budget = DocumentLoadBudget.Default with { MaxMarkupNodes = 1 },
        };

        DocumentLoadLimitException error = Assert.Throws<DocumentLoadLimitException>(
            () => MarkdownImporter.Import(Markdown, options));

        Assert.Equal(nameof(DocumentLoadBudget.MaxMarkupNodes), error.LimitName);
    }

    [Fact]
    public async Task ParsingNotesObservesCancellation()
    {
        string path = Path.Combine(Path.GetTempPath(), "quillwright-markdown-cancel-" + Guid.NewGuid().ToString("N") + ".md");
        try
        {
            await File.WriteAllTextAsync(
                path,
                "Body[^fn-1]\n\n[^fn-1]: note",
                TestContext.Current.CancellationToken);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cancellation.Cancel();

#pragma warning disable xUnit1051 // The independently cancelled token is the behavior under test.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MarkdownImporter.ImportFileAsync(
                path,
                new MarkdownImportOptions(),
                cancellation.Token));
#pragma warning restore xUnit1051
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GiantInlineHtmlTerminusScan_ObservesCancellationNearTheStart()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var parser = new MarkdownInlineParser(
            new Dictionary<string, (string Url, string? Title)>(),
            static (_, _, _) => null,
            static (_, _, _, _) => false,
            static (_, _) => { },
            new DocumentLoadBudgetState(DocumentLoadBudget.Default),
            cancellation.Token);
        string markdown = "<!--" + new string('x', 15_000_000) + " *emphasis*";
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task parse = Task.Run(
            () =>
            {
                started.SetResult();
                parser.Fill(new Paragraph(), markdown, RunFormat.Default, 1);
            },
            TestContext.Current.CancellationToken);

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await Task.Delay(1, TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parse);
    }

    [Fact]
    public void InlineRawHtmlMidParagraph_IsKeptAndPreciselyDiagnosed()
    {
        MarkdownImportResult result = MarkdownImporter.Import(
            "before <ins data-x=\"1\">kept</ins> after `<b>` and <https://example.org>");

        Assert.Equal(
            "before <ins data-x=\"1\">kept</ins> after <b> and https://example.org",
            result.Document.Paragraphs.Single().GetText());
        List<MarkdownImportWarning> warnings =
        [.. result.Diagnostics.Where(static warning => warning.Kind == MarkdownImportWarningKind.HtmlKeptAsText)];
        Assert.Equal(2, warnings.Count);
        Assert.Equal(["<ins data-x=\"1\">", "</ins>"], warnings.Select(static warning => warning.Subject));
        Assert.All(warnings, static warning => Assert.Equal(1, warning.Line));
    }

    [Theory]
    [InlineData("<!-- unclosed *emphasis*")]
    [InlineData("<? unclosed *emphasis*")]
    [InlineData("<![CDATA[ unclosed *emphasis*")]
    public void UnterminatedHtmlCandidates_RemainCommonMarkText(string markdown)
    {
        MarkdownImportResult result = MarkdownImporter.Import(markdown);

        Assert.True(result.Diagnostics.IsEmpty, result.Diagnostics.ToString());
        Assert.EndsWith(" unclosed emphasis", result.Document.Paragraphs.Single().GetText(), StringComparison.Ordinal);
        Assert.True(result.Document.Paragraphs.Single().Runs.Single(static run => run.Text == "emphasis").Format.Italic);
    }
}
