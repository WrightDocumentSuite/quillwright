using Quillwright.Diagnostics;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Doc;

/// <summary>Builds the model from the structures a legacy document is made of.</summary>
internal static partial class DocConverter
{
    public static WordDocument Convert(DocReadContext context)
    {
        WordDocument document = WordDocument.Create();
        (int dopOffset, int dopLength) = context.Fib.Properties;
        DocProperties.Apply(context.Table, dopOffset, dopLength, document.Settings);
        DocListTable.Read(context.Table, context.Fib.ListDefinitions, context.Fib.ListOverrides, document.Numbering);

        List<DocParagraph> paragraphs = ReadParagraphs(context, 0, context.Fib.MainTextLength);
        ReadBookmarks(document, context, paragraphs);
        ReadCommentRanges(context, paragraphs);
        BuildSections(document, context, paragraphs);

        ReadNotes(document, context, isEndnote: false);
        ReadNotes(document, context, isEndnote: true);
        ReadComments(document, context);
        ReadHeaders(document, context);
        ReadStrandedTextboxes(document, context);

        foreach (Section section in document.Sections)
        {
            if (section.Blocks.Count == 0)
                section.AddParagraph();
        }

        document.EmbeddedObjectList.AddRange(context.EmbeddedObjects);
        ReadCharts(document, context);
        ReportCommandTable(context);

        // The images are registered last because only what the text actually referred to has
        // been resolved by now; a picture the file keeps but no longer displays stays out.
        foreach (ImageData image in context.Images)
            document.Media.Add(image);

        document.WarningList.AddRange(context.Warnings);
        return document;
    }

    /// <summary>
    /// Says so when the document customises the toolbars or the keyboard. Those customisations
    /// are a table of command identifiers ([MS-DOC] 2.9.351, tabulated in [MS-CTDOC]) that
    /// belongs to an application this library is not, and no later format has anywhere to put
    /// them; a caller converting an archive is told rather than left to notice.
    /// </summary>
    private static void ReportCommandTable(DocReadContext context)
    {
        if (context.Fib.CommandTable.Length <= 0)
            return;

        context.Warn(
            WarningCode.PreservedVerbatim,
            "The document customises toolbars or key bindings; those customisations belong to Word itself and were left behind.");
    }

    /// <summary>
    /// Distributes the paragraphs of the main story over its sections. Section boundaries are
    /// character positions, and every paragraph that ends at or before one belongs to it.
    /// </summary>
    private static void BuildSections(WordDocument document, DocReadContext context, List<DocParagraph> paragraphs)
    {
        IReadOnlyList<DocSection> sections = context.Sections;
        if (sections.Count == 0)
        {
            BuildBlocks(document.Sections[0], paragraphs);
            return;
        }

        int cursor = 0;
        for (int i = 0; i < sections.Count; i++)
        {
            Section section = i == 0 ? document.Sections[0] : new Section();
            if (i > 0)
                document.Sections.Add(section);

            SprmTranslator.ApplySection(section.Properties, sections[i].Properties);

            int end = sections[i].EndPosition;
            var owned = new List<DocParagraph>();
            while (cursor < paragraphs.Count && (paragraphs[cursor].EndPosition <= end || i == sections.Count - 1))
                owned.Add(paragraphs[cursor++]);

            BuildBlocks(section, owned);
        }
    }

    /// <summary>
    /// Splits a story into paragraphs. Paragraph boundaries come from the formatting pages
    /// rather than from the text, because the text stream has no structure of its own.
    /// </summary>
    private static List<DocParagraph> ReadParagraphs(DocReadContext context, int startPosition, int endPosition)
    {
        var result = new List<DocParagraph>();
        if (endPosition <= startPosition)
            return result;

        int cursor = startPosition;
        foreach ((int endCp, FormattedRun run) in context.ParagraphRuns)
        {
            if (endCp <= cursor || endCp > endPosition)
                continue;

            result.Add(Build(context, cursor, endCp, run));
            cursor = endCp;
        }

        if (cursor < endPosition)
            result.Add(Build(context, cursor, endPosition, default));

        return result;
    }

    private static DocParagraph Build(DocReadContext context, int start, int end, FormattedRun run)
    {
        var format = ParagraphFormat.Default;
        DocParagraphFlags flags = default;
        byte[] properties = Resolve(context, run.Properties ?? []);
        if (properties.Length > 0)
            format = SprmTranslator.ApplyParagraph(format, properties, out flags);

        if (context.Styles.Identifier(run.StyleIndex) is { } styleId)
            format = format with { StyleId = styleId };

        var paragraph = new Paragraph { Format = format };
        foreach ((int from, int to, DocCharacterRun characters) in ReadRuns(context, start, end))
        {
            string text = context.Pieces.ReadText(context.Document, from, to, context.Ansi);
            AppendText(context, paragraph, text, characters, from);
        }

        DocHyperlinkReader.Collapse(paragraph);

        // The mark that ends a paragraph is what says whether it also ends a cell, and it is
        // stripped from the text, so it has to be read before that happens.
        string mark = end > start ? context.Pieces.ReadText(context.Document, end - 1, end, context.Ansi) : string.Empty;
        return new DocParagraph(paragraph, flags, end, mark is ['\u0007'], properties);
    }

    /// <summary>
    /// Follows the indirection a paragraph uses when its properties are too large to sit in
    /// a formatting page: one modifier holds an offset into the data stream, and the real
    /// property list is there.
    /// </summary>
    private static byte[] Resolve(DocReadContext context, byte[] properties)
    {
        var reader = new SprmReader(properties);
        while (reader.TryRead(out Sprm sprm))
        {
            if (sprm.Opcode != SprmCode.HugeParagraphProperties)
                continue;

            int at = sprm.Int32;
            if (at < 0 || at + 2 > context.Data.Length)
                break;

            int size = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(context.Data.AsSpan(at));
            if (size <= 0 || at + 2 + size > context.Data.Length)
                break;

            return context.Data.AsSpan(at + 2, size).ToArray();
        }

        return properties;
    }

    /// <summary>
    /// Splits a paragraph into stretches of uniform character formatting, using the run
    /// index the context built once for the whole file.
    /// </summary>
    private static IEnumerable<(int Start, int End, DocCharacterRun Run)> ReadRuns(DocReadContext context, int start, int end)
    {
        DocCharacterRun[] runs = context.CharacterRuns;
        int first = FindRun(runs, start);
        if (first < 0)
        {
            yield return (start, end, new DocCharacterRun(start, RunFormat.Default, -1, false));
            yield break;
        }

        for (int i = first; i < runs.Length && runs[i].Start < end; i++)
        {
            int from = Math.Max(runs[i].Start, start);
            int to = i + 1 < runs.Length ? Math.Min(runs[i + 1].Start, end) : end;
            if (to > from)
                yield return (from, to, runs[i]);
        }
    }

    /// <summary>Index of the run covering a position, found by binary search.</summary>
    private static int FindRun(DocCharacterRun[] runs, int position)
    {
        if (runs.Length == 0)
            return -1;

        int low = 0;
        int high = runs.Length - 1;
        int found = -1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (runs[middle].Start <= position)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found < 0 ? 0 : found;
    }

    private readonly record struct DocParagraph(
        Paragraph Paragraph,
        DocParagraphFlags Flags,
        int EndPosition,
        bool EndsCell,
        byte[] Properties);
}
