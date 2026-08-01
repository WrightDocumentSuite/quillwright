using Quillwright.Markdown;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Tests.Markdown;

public class MarkdownDocumentationTests
{
    [Fact]
    public void DocumentedCompleteExample_MatchesItsCheckedOutputFence()
    {
        WordDocument document = WordDocument.Create();
        document.Styles.GetOrAdd("Heading1");
        document.Sections[0].AddParagraph("Release notes", "Heading1");

        Paragraph intro = document.Sections[0].AddParagraph();
        intro.AppendText("Quillwright ", RunFormat.Default);
        intro.AppendText("exports", RunFormat.Default with { Bold = true });
        intro.AppendText(" Word documents.", RunFormat.Default);

        int bullets = document.Numbering.AddBulletList();
        AddListItem(document, bullets, "Preserves document order");
        AddListItem(document, bullets, "Returns sidecar images");

        var table = new Table();
        TableRow header = table.AddRow("Feature", "Result");
        header.Format = header.Format with { IsHeader = true };
        table.AddRow("Tables", "GFM or HTML");
        document.Sections[0].Blocks.Add(table);

        MarkdownDocument markdown = document.ToMarkdown();
        string expected = ExpectedOutputFromDocumentation();

        Assert.Equal(expected, markdown.Text);
    }

    private static void AddListItem(WordDocument document, int listId, string text)
    {
        Paragraph item = document.Sections[0].AddParagraph(text);
        item.Format = item.Format with { NumberingId = listId, NumberingLevel = 0 };
    }

    private static string ExpectedOutputFromDocumentation()
    {
        string path = Path.Combine(RepositoryRoot(), "docs", "markdown-export.md");
        string documentation = File.ReadAllText(path).ReplaceLineEndings("\n");
        const string startMarker = "<!-- expected-output:start -->\n```markdown\n";
        const string endMarker = "\n```\n<!-- expected-output:end -->";
        int start = documentation.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing expected-output start marker in {path}.");
        start += startMarker.Length;
        int end = documentation.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end >= 0, $"Missing expected-output end marker in {path}.");
        return documentation[start..end] + "\n";
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Quillwright.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the Quillwright repository root.");
    }
}
