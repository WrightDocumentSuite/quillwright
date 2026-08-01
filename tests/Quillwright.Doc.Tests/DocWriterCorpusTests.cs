using Quillwright.Diagnostics;
using Quillwright.Doc.Writing;
using Quillwright.Model;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Takes every document the reference repositories ship with, writes it back out as
/// <c>.doc</c>, and reads the result.
/// </summary>
/// <remarks>
/// Real documents exercise combinations no hand-written test thinks of — a table whose row
/// definition overflows a formatting page, a header with no content, a run whose font is not
/// in the font table. The bar is that the writer produces a file its own reader accepts and
/// that the visible text comes back, not that every property survives.
/// </remarks>
public class DocWriterCorpusTests
{
    private static readonly string CorpusRoot = ReferenceCorpus.Telerik;

    public static TheoryData<string> LegacyDocuments => Gather("*.doc");

    public static TheoryData<string> ModernDocuments => Gather("*.docx");

    [Theory]
    [MemberData(nameof(LegacyDocuments))]
    public async Task ALegacyDocument_CanBeWrittenBackAndReadAgain(string path)
    {
        Assert.SkipWhen(path.Length == 0, ReferenceCorpus.Absent);

        WordDocument document;
        try
        {
            document = await DocReader.LoadAsync(path, TestContext.Current.CancellationToken);
        }
        catch (Exception error) when (RefusedByDesign.Matches(error))
        {
            return;
        }

        WordDocument reopened = DocReader.Load(DocWriter.Save(document));

        Assert.Equal(Visible(document), Visible(reopened));
    }

    [Theory]
    [MemberData(nameof(ModernDocuments))]
    public async Task AModernDocument_CanBeWrittenAsLegacyAndReadAgain(string path)
    {
        Assert.SkipWhen(path.Length == 0, ReferenceCorpus.Absent);

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        WordDocument document;
        try
        {
            document = await WordDocument.LoadAsync(path, cancellationToken: cancellationToken);
        }
        catch (DocxFormatException)
        {
            return;
        }

        var warnings = new List<DocumentWarning>();
        byte[] file = DocWriter.Save(document, new DocWriteOptions { OnWarning = warnings.Add });
        WordDocument reopened = DocReader.Load(file);

        Assert.Equal(Visible(document), Visible(reopened));
    }

    /// <summary>
    /// The text a reader would see: no control characters, and no field instructions, which
    /// one format stores as content and the other does not.
    /// </summary>
    private static string Visible(WordDocument document)
    {
        var builder = new System.Text.StringBuilder();
        foreach (Block block in document.Sections.SelectMany(static s => s.Blocks))
            Append(builder, block);

        return builder.ToString();
    }

    private static void Append(System.Text.StringBuilder builder, Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                AppendVisibleText(builder, paragraph);
                break;
            case Table table:
                foreach (Block inner in table.Rows.SelectMany(static r => r.Cells).SelectMany(static c => c.Blocks))
                    Append(builder, inner);
                break;
            case BlockContentControl control:
                foreach (Block inner in control.Blocks)
                    Append(builder, inner);
                break;
        }
    }

    /// <summary>
    /// Appends a paragraph's text, skipping the instruction of every field: it is content in
    /// the model but not something a reader is shown. The words inside a text box are shown,
    /// and the binary format has no shape to keep them in, so both sides flatten them.
    /// </summary>
    private static void AppendVisibleText(System.Text.StringBuilder builder, Paragraph paragraph)
    {
        Dictionary<int, FieldCharKind> boundaries = paragraph.Objects
            .Where(static o => o.Object is FieldCharacter)
            .ToDictionary(static o => o.Offset, static o => ((FieldCharacter)o.Object).Kind);

        int depth = 0;
        ReadOnlySpan<char> text = paragraph.AsSpan();
        for (int i = 0; i < text.Length; i++)
        {
            if (boundaries.TryGetValue(i, out FieldCharKind kind))
            {
                depth = kind switch
                {
                    FieldCharKind.Begin => depth + 1,
                    FieldCharKind.Separate => Math.Max(0, depth - 1),
                    _ => depth,
                };
                continue;
            }

            if (depth != 0)
                continue;

            if (paragraph.ObjectAt(i) is Shape shape)
                AppendPlain(builder, shape.Content.GetText());
            else if (text[i] is not ('\r' or '\u0007' or InlineObject.Placeholder) && !char.IsControl(text[i]))
                builder.Append(text[i]);
        }
    }

    private static void AppendPlain(System.Text.StringBuilder builder, string text)
    {
        foreach (char c in text)
        {
            if (!char.IsControl(c))
                builder.Append(c);
        }
    }

    private static TheoryData<string> Gather(string pattern)
    {
        var data = new TheoryData<string>();
        if (Directory.Exists(CorpusRoot))
        {
            foreach (string path in Directory.EnumerateFiles(CorpusRoot, pattern, SearchOption.AllDirectories))
            {
                if (new FileInfo(path).Length is > 0 and < 8 * 1024 * 1024)
                    data.Add(path);
            }
        }

        // A theory with no cases does not fail and does not skip: it vanishes, and the total
        // quietly drops. One empty case keeps the skip visible.
        if (data.Count == 0)
            data.Add(string.Empty);

        return data;
    }
}
