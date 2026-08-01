using System.Text;
using Quillwright.Diagnostics;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Flattens the document model into the one character stream a legacy Word file stores, and
/// records where every paragraph, run, section and anchor falls in it.
/// </summary>
/// <remarks>
/// <para>
/// The binary format has no tree. Text is a single run of characters, and structure is
/// expressed by reserved characters inside it — a paragraph mark ends a paragraph, a cell
/// mark ends a cell, a page-break character ends a section — with formatting recorded
/// separately against character positions. Assembling is therefore a linearisation, and
/// almost every rule the format imposes is a rule about which character has to be where.
/// </para>
/// <para>
/// The stories follow one another in the order the header counts them: main text, footnotes,
/// headers, comments, endnotes. Positions are global across all of them, which is why the
/// assembler owns the whole stream rather than one story at a time.
/// </para>
/// </remarks>
internal sealed partial class StoryAssembler
{
    private readonly StringBuilder _text = new();
    private readonly DocWriteContext _context;
    private readonly List<ParagraphSpan> _paragraphs = [];
    private readonly List<RunSpanRecord> _runs = [];
    private readonly List<SectionSpan> _sections = [];

    public StoryAssembler(DocWriteContext context) => _context = context;

    /// <summary>The characters of every story, in the order the format lays them out.</summary>
    public string Text => _text.ToString();

    /// <summary>Number of characters written so far.</summary>
    public int Position => _text.Length;

    /// <summary>The paragraphs, cell marks and row marks, in position order.</summary>
    public IReadOnlyList<ParagraphSpan> Paragraphs => _paragraphs;

    /// <summary>The character-property runs, in position order.</summary>
    public IReadOnlyList<RunSpanRecord> Runs => _runs;

    /// <summary>The sections of the main story, in position order.</summary>
    public IReadOnlyList<SectionSpan> Sections => _sections;

    /// <summary>Characters in the main story.</summary>
    public int MainLength { get; private set; }

    /// <summary>Characters in the footnote story.</summary>
    public int FootnoteLength { get; private set; }

    /// <summary>Characters in the header story.</summary>
    public int HeaderLength { get; private set; }

    /// <summary>Characters in the comment story.</summary>
    public int CommentLength { get; private set; }

    /// <summary>Characters in the endnote story.</summary>
    public int EndnoteLength { get; private set; }

    /// <summary>Writes the main story: every section, with its page setup and its blocks.</summary>
    public void WriteMainStory(WordDocument document)
    {
        for (int i = 0; i < document.Sections.Count; i++)
        {
            Section section = document.Sections[i];
            _sections.Add(new SectionSpan(Position, SprmBuilder.BuildSection(section.Properties)));

            List<Block> blocks = [.. section.Blocks];

            // A section break lives on a paragraph mark, so a section that ends with a table
            // needs an empty paragraph to carry it — and so does an empty section.
            if (blocks.Count == 0 || blocks[^1] is not Paragraph)
                blocks.Add(new Paragraph());

            bool isLast = i == document.Sections.Count - 1;
            for (int b = 0; b < blocks.Count; b++)
            {
                bool endsSection = !isLast && b == blocks.Count - 1;
                WriteBlock(blocks[b], depth: 0, sectionMark: endsSection);
            }
        }

        MainLength = Position;
    }

    /// <summary>Appends a story that follows the main text, returning the range it occupies.</summary>
    /// <param name="blocks">The blocks of the story.</param>
    public HeaderStorySpan WriteStory(IEnumerable<Block> blocks)
    {
        int start = Position;
        foreach (Block block in blocks)
            WriteBlock(block, depth: 0, sectionMark: false);
        return new HeaderStorySpan(start, Position);
    }

    /// <summary>
    /// Appends the guard paragraph mark that separates the stories which follow the main
    /// text, and records how long each of them turned out.
    /// </summary>
    public void CloseStories(int footnotes, int headers, int comments, int endnotes)
    {
        FootnoteLength = footnotes;
        HeaderLength = headers;
        CommentLength = comments;
        EndnoteLength = endnotes;
    }

    /// <summary>Appends a bare paragraph mark, which several stories need as a guard.</summary>
    public void WriteGuardMark()
    {
        int start = Position;
        _text.Append(DocChar.ParagraphMark);
        _runs.Add(new RunSpanRecord(start, Position, []));
        _paragraphs.Add(new ParagraphSpan(Position, 0, []));
    }

    private void WriteBlock(Block block, int depth, bool sectionMark)
    {
        switch (block)
        {
            case Paragraph paragraph:
                WriteParagraph(paragraph, depth, sectionMark ? DocChar.PageBreak : DocChar.ParagraphMark, default);
                break;
            case Table table:
                _context.WriteTable(this, table, depth);
                break;
            case BlockContentControl control:
                foreach (Block inner in control.Blocks)
                    WriteBlock(inner, depth, sectionMark: false);
                break;
            case AlternateContentBlock alternate:
                // The binary format has no compatibility blocks, so the branch this version
                // reads is written and the alternatives go the way the wrapper does.
                foreach (Block inner in alternate.Blocks)
                    WriteBlock(inner, depth, sectionMark: false);
                break;
            case RawBlock raw:
                WriteRawBlock(raw, depth, sectionMark);
                break;
        }
    }

    /// <summary>
    /// Writes a block the model kept as markup. A display equation is the one such block with
    /// content worth recovering, so it becomes a paragraph of its text; anything else is
    /// dropped with a warning.
    /// </summary>
    private void WriteRawBlock(RawBlock raw, int depth, bool sectionMark)
    {
        if (OfficeMathText.Extract(raw.Xml) is not { } equation)
        {
            _context.Warn(WarningCode.PreservedVerbatim, "Markup preserved from a .docx package cannot be written to the binary format and was dropped.");
            return;
        }

        _context.Warn(WarningCode.PreservedVerbatim, "A display equation was flattened to its text; the binary format has no equation of its own.");
        WriteParagraph(new Paragraph(equation), depth, sectionMark ? DocChar.PageBreak : DocChar.ParagraphMark, default);
    }

    /// <summary>Writes one paragraph: its text, its mark, and the properties of both.</summary>
    internal void WriteParagraph(Paragraph paragraph, int depth, char mark, DocParagraphFlags flags)
    {
        WriteContent(paragraph);

        int markStart = Position;
        _text.Append(mark);
        _runs.Add(new RunSpanRecord(markStart, Position, _context.BuildRun(paragraph.MarkFormat)));

        int styleIndex = _context.Styles.IndexOf(paragraph.Format.StyleId, _context.Document.Styles);
        DocParagraphFlags actual = depth > 0 ? flags with { InTable = true, TableDepth = depth } : flags;

        byte[] properties = SprmBuilder.BuildParagraph(paragraph.Format, actual);
        _context.Lists.Apply(paragraph, ref properties);
        _paragraphs.Add(new ParagraphSpan(Position, styleIndex, properties));
    }

}
