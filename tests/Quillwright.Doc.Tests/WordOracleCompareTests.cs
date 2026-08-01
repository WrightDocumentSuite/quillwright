using System.Globalization;
using System.Runtime.Versioning;
using Quillwright.Editing;
using Quillwright.Model;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Puts a comparison result in front of Word itself: the redline must be revisions Word
/// counts and can accept, not merely markup this library's own reader likes.
/// </summary>
[Trait("Category", "word-oracle")]
[SupportedOSPlatform("windows")]
public class WordOracleCompareTests
{
    [Fact]
    public async Task ARedline_ShowsItsRevisionsInWord()
    {
        Assert.SkipUnless(WordOracle.Enabled, "Set QUILLWRIGHT_WORD_ORACLE=1 and install Word to run the oracle tests.");

        WordDocument original = WordDocument.Create();
        original.Sections[0].AddParagraph("The quick brown fox jumps over the lazy dog.");
        original.Sections[0].AddParagraph("A paragraph that will go.");

        WordDocument revised = WordDocument.Create();
        revised.Sections[0].AddParagraph("The quick red fox jumps over the lazy dog.");
        revised.Sections[0].AddParagraph("A paragraph that arrived.");

        ComparisonResult result = DocumentComparer.Compare(original, revised, new DocumentCompareOptions { Author = "Oracle" });

        string path = Path.Combine(Path.GetTempPath(), $"quillwright-oracle-{Guid.NewGuid():N}.docx");
        await result.Document.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        object count = WordOracle.Inspect(path, opened => WordOracle.Get(WordOracle.Get(opened, "Revisions")!, "Count")!);

        Assert.True(Convert.ToInt32(count, CultureInfo.InvariantCulture) >= 2, $"Word counted {count} revisions.");
    }
}
