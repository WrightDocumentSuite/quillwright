using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Doc.Tests;

public class DocReaderTests
{
    private static readonly string CorpusRoot = ReferenceCorpus.Telerik;

    [Fact]
    public void NotACompoundFile_IsRefusedWithAClearMessage()
    {
        DocFormatException error = Assert.Throws<DocFormatException>(() => DocReader.Load([1, 2, 3, 4, 5, 6, 7, 8, 9]));
        Assert.Contains("compound file", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LegacyDocuments_YieldReadableText()
    {
        Assert.SkipUnless(Directory.Exists(CorpusRoot), ReferenceCorpus.Absent);

        var withText = 0;
        var examined = 0;
        foreach (string path in Directory.EnumerateFiles(CorpusRoot, "*.doc", SearchOption.AllDirectories).Take(60))
        {
            examined++;
            try
            {
                WordDocument document = await DocReader.LoadAsync(path, TestContext.Current.CancellationToken);
                if (document.GetText().Any(char.IsLetterOrDigit))
                    withText++;
            }
            catch (Exception error) when (RefusedByDesign.Matches(error))
            {
                examined--;
            }
        }

        // A test corpus holds deliberately empty documents too, so the bar is that most of
        // them come back with something a reader would recognise as their content.
        Assert.True(withText * 2 > examined, $"Only {withText} of {examined} documents produced text.");
    }

    [Fact]
    public async Task LegacyDocuments_CarryFormattingAcross()
    {
        Assert.SkipUnless(Directory.Exists(CorpusRoot), ReferenceCorpus.Absent);

        var bold = 0;
        var sized = 0;
        var styled = 0;
        foreach (string path in Directory.EnumerateFiles(CorpusRoot, "*.doc", SearchOption.AllDirectories).Take(80))
        {
            WordDocument document;
            try
            {
                document = await DocReader.LoadAsync(path, TestContext.Current.CancellationToken);
            }
            catch (Exception error) when (RefusedByDesign.Matches(error))
            {
                continue;
            }

            foreach (Paragraph paragraph in document.Paragraphs)
            {
                if (paragraph.Format.StyleId is not null)
                    styled++;
                foreach (Run run in paragraph.Runs)
                {
                    if (run.Format.Bold == true)
                        bold++;
                    if (run.Format.Size is not null)
                        sized++;
                }
            }
        }

        Assert.True(bold > 0, "No bold run survived the conversion.");
        Assert.True(sized > 0, "No font size survived the conversion.");
        Assert.True(styled > 0, "No paragraph style survived the conversion.");
    }

    [Fact]
    public async Task LegacyTables_BecomeTables()
    {
        Assert.SkipUnless(Directory.Exists(CorpusRoot), ReferenceCorpus.Absent);

        var tables = 0;
        foreach (string path in Directory.EnumerateFiles(CorpusRoot, "*.doc", SearchOption.AllDirectories))
        {
            try
            {
                WordDocument document = await DocReader.LoadAsync(path, TestContext.Current.CancellationToken);
                tables += document.Sections.SelectMany(static section => section.Tables).Count();
            }
            catch (Exception error) when (RefusedByDesign.Matches(error))
            {
                // Refused files are not part of this measurement.
            }

            if (tables > 0)
                break;
        }

        Assert.True(tables > 0, "No table survived the conversion.");
    }
}
