using Quillwright.Editing;
using Quillwright.Model;

namespace Quillwright.Samples;

/// <summary>Saves a redline and derives separate accepted and rejected versions.</summary>
internal static class CompareRevisions
{
    public static async Task RunAsync(string directory)
    {
        WordDocument original = WordDocument.Create();
        original.Sections[0].AddParagraph("Delivery takes ten days.");
        WordDocument revised = WordDocument.Create();
        revised.Sections[0].AddParagraph("Delivery takes five working days.");

        ComparisonResult comparison = DocumentComparer.Compare(original, revised,
            new DocumentCompareOptions { Author = "Alex Reviewer" });
        string redline = Path.Combine(directory, "08-redline.docx");
        await comparison.Document.SaveAsync(redline);

        WordDocument accepted = await WordDocument.LoadAsync(redline);
        accepted.AcceptAllRevisions();
        await accepted.SaveAsync(Path.Combine(directory, "08-accepted.docx"));
        WordDocument rejected = await WordDocument.LoadAsync(redline);
        rejected.RejectAllRevisions();
        await rejected.SaveAsync(Path.Combine(directory, "08-rejected.docx"));

        string acceptedText = string.Join("\n", accepted.Paragraphs.Select(p => p.Text));
        string rejectedText = string.Join("\n", rejected.Paragraphs.Select(p => p.Text));
        if (acceptedText != string.Join("\n", revised.Paragraphs.Select(p => p.Text)) ||
            rejectedText != string.Join("\n", original.Paragraphs.Select(p => p.Text)))
            throw new InvalidOperationException("Revision resolution did not reproduce the source texts.");

        Console.WriteLine($"  Redline: {comparison.Insertions} insertion(s), {comparison.Deletions} deletion(s); both resolutions verified");
    }
}
