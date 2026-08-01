using System.IO.Compression;
using System.Text.RegularExpressions;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Tests;

/// <summary>
/// A Strict package names every role under <c>purl.oclc.org</c>.
/// </summary>
/// <remarks>
/// Reading normalises those names so the model works in one vocabulary, but writing puts
/// them back as they were. Converting only the parts the model regenerates would leave the
/// copied ones — themes, charts, the glossary — speaking Strict inside a file whose main
/// part had turned Transitional, which no consumer expects.
/// </remarks>
public class StrictPackageTests
{
    [Fact]
    public async Task StrictPackage_StaysStrictAndConsistent()
    {
        string strictRoot = ReferenceCorpus.RequireOpenXmlPath("test/DocumentFormat.OpenXml.Tests.Assets/assets/TestDataStorage/O14ISOStrict");

        var offenders = new List<string>();
        foreach (string path in Directory.EnumerateFiles(strictRoot, "*.docx", SearchOption.AllDirectories).Take(20))
        {
            WordDocument document = await WordDocument.LoadAsync(path, cancellationToken: TestContext.Current.CancellationToken);
            using MemoryStream saved = await DocumentFixture.SaveAsync(document);

            foreach (string part in (string[])["word/document.xml", "word/styles.xml"])
            {
                string head = ReadEntry(saved, part, 600);
                if (!head.Contains("purl.oclc.org", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(path)} :: {part} lost its Strict vocabulary");
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The namespace is only half of it. Strict renamed the direction words that assume a
    /// left-to-right page, so <c>CT_Ind</c> takes <c>start</c> and <c>end</c>, a table or cell
    /// border box takes <c>w:start</c> and <c>w:end</c>, and <c>ST_Jc</c> dropped
    /// <c>left</c> and <c>right</c> from its enumeration altogether. Writing the Transitional
    /// spelling under the Strict namespace produces a part no schema accepts.
    /// </summary>
    /// <remarks>
    /// <c>CT_PBdr</c> and <c>CT_PageMar</c> are deliberately not in the list: those two kept
    /// <c>left</c> and <c>right</c> in Strict, so renaming them would be the same mistake in
    /// the other direction.
    /// </remarks>
    [Fact]
    public async Task StrictPackage_KeepsTheStrictSpellingOfEveryRenamedName()
    {
        string strictRoot = ReferenceCorpus.RequireOpenXmlPath("test/DocumentFormat.OpenXml.Tests.Assets/assets/TestDataStorage/O14ISOStrict");

        (string Name, string Pattern)[] transitional =
        [
            ("w:ind/@w:left", "<w:ind[^>]*\\sw:left="),
            ("w:ind/@w:right", "<w:ind[^>]*\\sw:right="),
            ("w:ind/@w:leftChars", "<w:ind[^>]*\\sw:leftChars="),
            ("w:ind/@w:rightChars", "<w:ind[^>]*\\sw:rightChars="),
            ("w:jc w:val=\"left\"", "<w:jc\\s+w:val=\"(left|right)\""),
            ("w:tblBorders/w:left", "<w:tblBorders>(?:(?!</w:tblBorders>).)*<w:(left|right)[\\s/>]"),
            ("w:tcBorders/w:left", "<w:tcBorders>(?:(?!</w:tcBorders>).)*<w:(left|right)[\\s/>]"),
            ("w:tblCellMar/w:left", "<w:tblCellMar>(?:(?!</w:tblCellMar>).)*<w:(left|right)[\\s/>]"),
            ("w:tcMar/w:left", "<w:tcMar>(?:(?!</w:tcMar>).)*<w:(left|right)[\\s/>]"),
        ];

        var offenders = new List<string>();
        foreach (string path in Directory.EnumerateFiles(strictRoot, "*.docx", SearchOption.AllDirectories).Take(20))
        {
            WordDocument document = await WordDocument.LoadAsync(path, cancellationToken: TestContext.Current.CancellationToken);
            using MemoryStream saved = await DocumentFixture.SaveAsync(document);

            foreach (string part in (string[])["word/document.xml", "word/styles.xml", "word/numbering.xml"])
            {
                string markup = ReadEntry(saved, part);
                foreach ((string name, string pattern) in transitional)
                {
                    if (Regex.IsMatch(markup, pattern, RegexOptions.Singleline))
                        offenders.Add($"{Path.GetFileName(path)} :: {part} wrote {name}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>A Transitional package keeps the spelling it came with, unchanged.</summary>
    [Fact]
    public async Task TransitionalPackage_KeepsTheTransitionalSpelling()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("indented");
        paragraph.Format = paragraph.Format with
        {
            IndentLeft = Length.FromTwips(720),
            Alignment = ParagraphAlignment.Right,
        };

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        string markup = ReadEntry(saved, "word/document.xml");

        Assert.Contains("w:left=\"720\"", markup, StringComparison.Ordinal);
        Assert.Contains("w:val=\"right\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("w:start=\"720\"", markup, StringComparison.Ordinal);
    }

    private static string ReadEntry(Stream package, string name, int limit = 0)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.GetEntry(name) is not { } entry)
            return string.Empty;

        using var reader = new StreamReader(entry.Open());
        if (limit == 0)
            return reader.ReadToEnd();

        char[] head = new char[limit];
        int read = reader.Read(head, 0, head.Length);
        return new string(head, 0, read);
    }
}
