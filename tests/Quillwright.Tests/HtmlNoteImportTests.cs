using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Quillwright.Diagnostics;
using Quillwright.Html;
using Quillwright.Model;

namespace Quillwright.Tests;

public sealed partial class HtmlNoteImportTests
{
    [Fact]
    public async Task ExportedNotes_ReimportAsNotesWithRepeatedReferencesAndParagraphs()
    {
        WordDocument source = WordDocument.Create();
        Paragraph body = source.Sections[0].AddParagraph("Body");
        Note footnote = source.AddFootnote(body, "First paragraph.");
        footnote.AddParagraph("Second paragraph.");
        body.AppendText(" again ");
        body.AppendObject(new NoteReference { Id = footnote.Id });
        body.AppendText(" end ");
        Note endnote = source.AddEndnote(body, "Endnote paragraph.");

        string html = source.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;
        string[] ids = [.. IdAttribute().Matches(html).Cast<Match>().Select(static match => match.Groups[1].Value)];

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains($"id=\"fn-{footnote.Id}-1-ref-2\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"#fn-{footnote.Id}-1-ref-2\"", html, StringComparison.Ordinal);

        HtmlImportResult imported = HtmlImporter.Import(html);

        Assert.Empty(imported.Diagnostics);
        Note importedFootnote = Assert.Single(imported.Document.Footnotes, static note => note.Kind == NoteKind.Normal);
        Note importedEndnote = Assert.Single(imported.Document.Endnotes, static note => note.Kind == NoteKind.Normal);
        Assert.Equal(footnote.Id, importedFootnote.Id);
        Assert.Equal(endnote.Id, importedEndnote.Id);
        Assert.Equal([" First paragraph.", "Second paragraph."], importedFootnote.Blocks.Paragraphs.Select(static p => p.GetText()));
        Assert.Equal(" Endnote paragraph.", importedEndnote.Blocks.Paragraphs.Single().GetText());

        NoteReference[] references = [.. imported.Document.Paragraphs.Single().Objects
            .Select(static anchored => anchored.Object)
            .OfType<NoteReference>()];
        Assert.Equal(3, references.Length);
        Assert.False(references[0].IsEndnote);
        Assert.False(references[1].IsEndnote);
        Assert.True(references[2].IsEndnote);
        Assert.Equal(references[0].Id, references[1].Id);

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(imported.Document, "HTML notes");
        Assert.Single(reloaded.Footnotes, static note => note.Kind == NoteKind.Normal);
        Assert.Single(reloaded.Endnotes, static note => note.Kind == NoteKind.Normal);
        Assert.Equal(3, reloaded.Paragraphs.Single().Objects.Count(static anchored => anchored.Object is NoteReference));
    }

    [Fact]
    public void MalformedDuplicateAndDanglingNotes_AreDeterministicAndDiagnosed()
    {
        const string html = """
            <p>known<sup id="fn-7-1-ref"><a href="#fn-7-1">1</a></sup>
            dangling<sup id="fn-404-2-ref"><a href="#fn-404-2">2</a></sup></p>
            <hr>
            <section class="footnotes"><ol>
              <li id="fn-7-1"><p>Known body.</p><a href="#fn-7-1-ref">↩</a></li>
              <li id="fn-7-1"><p>Duplicate body.</p><a href="#fn-7-1-ref">↩</a></li>
              <li id="fn-bad"><p>Malformed body.</p></li>
              <li id="en-3-3"><p>Unused body.</p><a href="#en-3-3-ref">↩</a></li>
            </ol></section>
            """;

        HtmlImportResult result = HtmlImporter.Import(html);

        Note footnote = Assert.Single(result.Document.Footnotes, static note => note.Kind == NoteKind.Normal);
        Assert.Equal(7, footnote.Id);
        Assert.Equal(" Known body.", footnote.Blocks.Paragraphs.Single().GetText());
        Assert.DoesNotContain(result.Document.Endnotes, static note => note.Kind == NoteKind.Normal);
        Assert.Contains("Duplicate body", result.Document.GetText(), StringComparison.Ordinal);
        Assert.Contains("Malformed body", result.Document.GetText(), StringComparison.Ordinal);
        Assert.Contains("Unused body", result.Document.GetText(), StringComparison.Ordinal);

        Paragraph body = result.Document.Sections[0].Blocks.OfType<Paragraph>()
            .First(static paragraph => paragraph.GetText().Contains("known", StringComparison.Ordinal));
        NoteReference reference = Assert.Single(body.Objects.Select(static anchored => anchored.Object).OfType<NoteReference>());
        Assert.Equal(7, reference.Id);
        Assert.Contains("2", body.GetText(), StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, static warning =>
            warning.Kind == HtmlImportWarningKind.NoteMalformed && warning.Subject == "fn-7-1");
        Assert.Contains(result.Diagnostics, static warning =>
            warning.Kind == HtmlImportWarningKind.NoteMalformed && warning.Subject == "fn-7-1-ref" &&
            warning.Message.Contains("first definition wins", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, static warning =>
            warning.Kind == HtmlImportWarningKind.NoteMalformed && warning.Subject == "fn-bad");
        Assert.Contains(result.Diagnostics, static warning =>
            warning.Kind == HtmlImportWarningKind.NoteDangling && warning.Subject == "fn-404-2");
        Assert.Contains(result.Diagnostics, static warning =>
            warning.Kind == HtmlImportWarningKind.NoteDangling && warning.Subject == "en-3-3");
    }

    [Fact]
    public void FakeAndMixedFootnoteSections_KeepOrdinaryContentAndTheirSeparator()
    {
        const string html = """
            <p>Known<sup id="fn-7-1-ref"><a href="#fn-7-1">1</a></sup></p>
            <hr>
            <section class="footnotes"><ol>
              <li id="fn-7-1"><p>Known body.</p><a href="#fn-7-1-ref">↩</a></li>
              <li id="fn-8-2"><p>Unpaired definition stays visible.</p><a href="#fn-8-2-ref">↩</a></li>
              <li><p>Ordinary list item stays visible.</p></li>
            </ol><p>Mixed sibling stays visible.</p></section>
            <hr>
            <section class="footnotes"><ol>
              <li id="fn-9-3"><p>Fake note stays visible.
                <sup id="fn-9-3-ref"><a href="#fn-9-3">3</a></sup></p>
                <a href="#fn-9-3-ref">↩</a></li>
            </ol></section>
            """;

        HtmlImportResult result = HtmlImporter.Import(html);

        Note note = Assert.Single(result.Document.Footnotes, static candidate => candidate.Kind == NoteKind.Normal);
        Assert.Equal(7, note.Id);
        string mainStory = string.Join('\n', result.Document.Sections[0].Blocks
            .OfType<Paragraph>()
            .Select(static paragraph => paragraph.GetText()));
        Assert.DoesNotContain("Known body", mainStory, StringComparison.Ordinal);
        Assert.Contains("Unpaired definition stays visible.", mainStory, StringComparison.Ordinal);
        Assert.Contains("Ordinary list item stays visible.", mainStory, StringComparison.Ordinal);
        Assert.Contains("Mixed sibling stays visible.", mainStory, StringComparison.Ordinal);
        Assert.Contains("Fake note stays visible.", mainStory, StringComparison.Ordinal);
        Assert.Equal(2, result.Document.Sections[0].Blocks.OfType<Paragraph>()
            .Count(static paragraph => paragraph.Format.Borders?.Bottom is { IsEmpty: false }));
    }

    [Fact]
    public void FullyReciprocalGeneratedSection_ConsumesOnlyItsOwnSeparatorAndDefinition()
    {
        const string html = """
            <p>Body<sup id="fn-3-1-ref"><a href="#fn-3-1">1</a></sup></p>
            <hr>
            <section class="footnotes"><ol>
              <li id="fn-3-1"><p>Note body.</p><a href="#fn-3-1-ref">↩</a></li>
            </ol></section>
            """;

        HtmlImportResult result = HtmlImporter.Import(html);

        Assert.Empty(result.Diagnostics);
        Assert.Equal("Body", Assert.Single(result.Document.Sections[0].Blocks.OfType<Paragraph>()).GetText());
        Assert.DoesNotContain(result.Document.Sections[0].Blocks.OfType<Paragraph>(),
            static paragraph => paragraph.Format.Borders?.Bottom is { IsEmpty: false });
        Assert.Equal(" Note body.", Assert.Single(result.Document.Footnotes,
            static note => note.Kind == NoteKind.Normal).Blocks.Paragraphs.Single().GetText());
    }

    [Fact]
    public void ManySiblingFootnoteSections_StayWithinTheLinearImportEnvelope()
    {
        const int SectionCount = 8_000;
        var html = new StringBuilder(SectionCount * 96);
        for (int index = 0; index < SectionCount; index++)
        {
            html.Append("<section class=\"footnotes\"><ol><li id=\"fn-")
                .Append(index + 1)
                .Append("-1\"></li></ol></section>");
        }

        var stopwatch = Stopwatch.StartNew();
        HtmlImportResult result = HtmlImporter.Import(html.ToString());
        stopwatch.Stop();

        Assert.Equal(SectionCount, result.Diagnostics.Count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"Import took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void DuplicateDefinitionsWithDistinctBacklinks_UseThePairIndex()
    {
        const int DefinitionCount = 20_000;
        var html = new StringBuilder(DefinitionCount * 150);
        html.Append("<p>");
        for (int index = 0; index < DefinitionCount; index++)
        {
            string referenceId = index == 0 ? "fn-1-1-ref" : $"fn-1-1-ref-{index + 1}";
            html.Append("<sup id=\"").Append(referenceId)
                .Append("\"><a href=\"#fn-1-1\">1</a></sup>");
        }

        html.Append("</p><section class=\"footnotes\"><ol>");
        for (int index = 0; index < DefinitionCount; index++)
        {
            string referenceId = index == 0 ? "fn-1-1-ref" : $"fn-1-1-ref-{index + 1}";
            html.Append("<li id=\"fn-1-1\"><a href=\"#").Append(referenceId)
                .Append("\">↩</a></li>");
        }

        html.Append("</ol></section>");

        var stopwatch = Stopwatch.StartNew();
        HtmlImportResult result = HtmlImporter.Import(html.ToString());
        stopwatch.Stop();

        Assert.Single(result.Document.Footnotes, static note => note.Kind == NoteKind.Normal);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"Import took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task ManySiblingFootnoteSections_HonorAsyncImportCancellation()
    {
        const int SectionCount = 100_000;
        var html = new StringBuilder(SectionCount * 64);
        for (int index = 0; index < SectionCount; index++)
            html.Append("<section class=\"footnotes\"></section>");

        string path = Path.Combine(Path.GetTempPath(), $"quillwright-html-note-scale-{Guid.NewGuid():N}.html");
        try
        {
            await File.WriteAllTextAsync(path, html.ToString(), TestContext.Current.CancellationToken);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(1));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                HtmlImporter.ImportFileAsync(path, cancellationToken: cancellation.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task NoteImport_UsesTheSharedMarkupBudgetAndCancellation()
    {
        const string html = """
            <p>body<sup id="fn-1-1-ref"><a href="#fn-1-1">1</a></sup></p>
            <section class="footnotes"><ol><li id="fn-1-1"><p>note</p>
            <a href="#fn-1-1-ref">↩</a></li></ol></section>
            """;

        var limited = new HtmlImportOptions
        {
            Budget = DocumentLoadBudget.Default with { MaxMarkupNodes = 8 },
        };
        DocumentLoadLimitException exception = Assert.Throws<DocumentLoadLimitException>(() =>
            HtmlImporter.Import(html, limited));
        Assert.Equal(nameof(DocumentLoadBudget.MaxMarkupNodes), exception.LimitName);

        string path = Path.Combine(Path.GetTempPath(), $"quillwright-html-note-{Guid.NewGuid():N}.html");
        try
        {
            await File.WriteAllTextAsync(path, html, TestContext.Current.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                HtmlImporter.ImportFileAsync(path, cancellationToken: cancellation.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TableCaption_RoundTripsThroughHtmlAndDocx()
    {
        WordDocument source = WordDocument.Create();
        Table table = Table.Create(1, 1);
        table.Format = table.Format with { Caption = "Quarterly <results>" };
        source.Sections[0].Blocks.Add(table);

        string html = source.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;
        Assert.Contains("<table>\n<caption>Quarterly &lt;results&gt;</caption>", html, StringComparison.Ordinal);

        WordDocument imported = HtmlImporter.Import(html).Document;
        Table importedTable = Assert.Single(imported.Sections[0].Blocks.OfType<Table>());
        Assert.Equal("Quarterly <results>", importedTable.Format.Caption);

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(imported, "HTML table caption");
        Assert.Equal("Quarterly <results>", Assert.Single(reloaded.Sections[0].Blocks.OfType<Table>()).Format.Caption);
    }

    [Fact]
    public void NoteBodies_CanDiscoverLaterNotesAndAddBacklinksToEarlierNotes()
    {
        WordDocument source = WordDocument.Create();
        Paragraph body = source.Sections[0].AddParagraph("Body");
        Note first = source.AddFootnote(body, "First.");
        var later = new Note(source, isEndnote: false) { Id = first.Id + 1 };
        later.AddParagraph("Later.");
        source.FootnoteList.Add(later);

        first.Blocks.Paragraphs.Single().AppendObject(new NoteReference { Id = later.Id });
        later.Blocks.Paragraphs.Single().AppendObject(new NoteReference { Id = first.Id });
        later.Blocks.Paragraphs.Single().AppendObject(new NoteReference { Id = later.Id });

        string html = source.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;

        Assert.Contains($"id=\"fn-{later.Id}-2\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"#fn-{first.Id}-1-ref-2\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"#fn-{later.Id}-2-ref-2\"", html, StringComparison.Ordinal);
        string[] ids = [.. IdAttribute().Matches(html).Cast<Match>().Select(static match => match.Groups[1].Value)];
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());

        HtmlImportResult imported = HtmlImporter.Import(html);
        Assert.Empty(imported.Diagnostics);
        Note[] notes = [.. imported.Document.Footnotes.Where(static note => note.Kind == NoteKind.Normal)];
        Assert.Equal(2, notes.Length);
        Assert.Single(notes[0].Blocks.Paragraphs.Single().Objects, static anchored => anchored.Object is NoteReference);
        Assert.Equal(2, notes[1].Blocks.Paragraphs.Single().Objects.Count(static anchored => anchored.Object is NoteReference));
    }

    [Fact]
    public void PreformattedNumericCarriageReturns_AreNormalizedToLineFeeds()
    {
        WordDocument document = HtmlImporter.Import("<pre>a&#13;\nb&#13;c</pre>").Document;

        Assert.Equal("a\nb\nc", document.Paragraphs.Single().Text);
    }

    [GeneratedRegex("\\sid=\\\"([^\\\"]+)\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex IdAttribute();
}
