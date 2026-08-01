using Quillwright.Diagnostics;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Doc.Writing;

/// <summary>
/// The state shared by everything that writes one document: the tables that hand out
/// indexes, the anchors collected while the text is assembled, and where losses are
/// reported.
/// </summary>
internal sealed class DocWriteContext
{
    private readonly DocWriteOptions _options;
    private readonly Dictionary<int, int> _openBookmarks = [];
    private readonly Dictionary<int, int> _openCommentRanges = [];

    public DocWriteContext(WordDocument document, DocWriteOptions options)
    {
        Document = document;
        _options = options;
        Lists = new ListBuilder(this);
        Pictures = new PictureBuilder(this);
    }

    /// <summary>The document being written.</summary>
    public WordDocument Document { get; }

    /// <summary>The fonts every character property refers to by index.</summary>
    public FontTableBuilder Fonts { get; } = new();

    /// <summary>The styles every paragraph and run refers to by index.</summary>
    public StyleSheetBuilder Styles { get; } = new();

    /// <summary>The list definitions numbered paragraphs refer to.</summary>
    public ListBuilder Lists { get; }

    /// <summary>The pictures, and the data stream they live in.</summary>
    public PictureBuilder Pictures { get; }

    /// <summary>Field boundaries, in position order.</summary>
    public List<FieldSpan> Fields { get; } = [];

    /// <summary>Bookmarks, collected as their two ends are met.</summary>
    public List<BookmarkSpan> Bookmarks { get; } = [];

    /// <summary>Footnote references and the position of each note's body.</summary>
    public List<NoteSpan> Footnotes { get; } = [];

    /// <summary>Endnote references and the position of each note's body.</summary>
    public List<NoteSpan> Endnotes { get; } = [];

    /// <summary>Comment references and the position of each comment's body.</summary>
    public List<NoteSpan> Comments { get; } = [];

    /// <summary>The stretches of text the comments apply to.</summary>
    public List<CommentRangeSpan> CommentRanges { get; } = [];

    /// <summary>Where each header, footer and note separator story begins.</summary>
    public List<int> HeaderStories { get; } = [];

    /// <summary>Whether hyperlinks are written as fields.</summary>
    public bool WriteHyperlinks => _options.WriteHyperlinks;

    /// <summary>Whether pictures are written at all.</summary>
    public bool WriteImages => _options.WriteImages;

    /// <summary>Reports content that could not be written as itself.</summary>
    public void Warn(WarningCode code, string message) =>
        _options.OnWarning?.Invoke(new DocumentWarning(code, message));

    /// <summary>Builds the character modifiers of a run, resolving fonts and styles to indexes.</summary>
    public byte[] BuildRun(RunFormat format) =>
        SprmBuilder.BuildRun(format, Fonts.IndexOf, id => Styles.IndexOf(id, Document.Styles));

    /// <summary>Builds the character modifiers of a run and marks it as carrying a reserved character.</summary>
    public byte[] BuildSpecialRun(RunFormat format, Action<GrpprlWriter>? extra = null)
    {
        var writer = new GrpprlWriter();
        writer.Append(BuildRun(format));
        writer.Toggle(SprmCode.Special, true);
        extra?.Invoke(writer);
        return writer.ToArray();
    }

    /// <summary>Records a zero-width mark met while assembling the text.</summary>
    public void NoteMark(InlineMark mark, int position)
    {
        switch (mark)
        {
            case BookmarkStart start:
                _openBookmarks[start.Id] = position;
                Bookmarks.Add(new BookmarkSpan(start.Name, position, position));
                break;
            case BookmarkEnd end when _openBookmarks.TryGetValue(end.Id, out int opened):
                CloseBookmark(opened, position);
                break;

            // A comment's extent is not part of the comment: the format stores it as a
            // bookmark of its own that the comment's record points at.
            case CommentRangeStart start:
                _openCommentRanges[start.Id] = position;
                break;
            case CommentRangeEnd end when _openCommentRanges.Remove(end.Id, out int from):
                CommentRanges.Add(new CommentRangeSpan(end.Id, from, position));
                break;
        }
    }

    /// <summary>Writes an anchored object as the reserved character the format uses for it.</summary>
    public void WriteObject(StoryAssembler story, InlineObject anchored, RunFormat format)
    {
        switch (anchored)
        {
            case Picture picture:
                Pictures.Write(story, picture, format);
                break;
            case NoteReference note:
                story.WriteSpecial(DocChar.NoteReference, BuildSpecialRun(format));
                (note.IsEndnote ? Endnotes : Footnotes)
                    .Add(new NoteSpan(story.Position - 1, note.Id, -1, note.CustomMark));
                break;
            case NoteNumberMark:
                // The number a note prints for itself is the same character as a reference,
                // told apart only by not appearing in the list of references.
                story.WriteSpecial(DocChar.NoteReference, BuildSpecialRun(format));
                break;
            case CommentReference reference:
                story.WriteSpecial(DocChar.CommentReference, BuildSpecialRun(format));
                Comments.Add(new NoteSpan(story.Position - 1, reference.Id, -1));
                break;
            case FieldCharacter field:
                WriteFieldCharacter(story, field, format);
                break;
            case SymbolCharacter symbol:
                story.WriteText(char.ConvertFromUtf32(Math.Clamp(symbol.Character, 32, 0xFFFF)), BuildRun(format));
                break;
            // The binary format has no compatibility block: the branch this version selected
            // is the one that survives, and the alternatives have nowhere to go.
            case AlternateContent alternate:
                WriteObject(story, alternate.Content, format);
                break;
            case Shape shape:
                Warn(WarningCode.PreservedVerbatim, "A text box was flattened to its text; its shape is not converted.");
                story.WriteText(shape.Content.GetText(), BuildRun(format));
                break;
            case MathObject math when OfficeMathText.Flatten(math) is { } formula:
                Warn(WarningCode.PreservedVerbatim, "An equation was flattened to its text; the binary format has no equation of its own.");
                story.WriteText(formula, BuildRun(format));
                break;
            case RawInline raw when OfficeMathText.Extract(raw.Xml) is { } equation:
                Warn(WarningCode.PreservedVerbatim, "An equation was flattened to its text; the binary format has no equation of its own.");
                story.WriteText(equation, BuildRun(format));
                break;
            case RenderedPageBreak or NoteSeparator or PositionalTab:
                break;
            default:
                Warn(WarningCode.PreservedVerbatim, $"{anchored.GetType().Name} has no equivalent in the binary format and was dropped.");
                break;
        }
    }

    /// <summary>Writes a table as the sequence of marked paragraphs the format stores it as.</summary>
    public void WriteTable(StoryAssembler story, Table table, int depth) =>
        TableWriter.Write(this, story, table, depth + 1);

    private void CloseBookmark(int start, int end)
    {
        for (int i = Bookmarks.Count - 1; i >= 0; i--)
        {
            if (Bookmarks[i].StartPosition != start || Bookmarks[i].EndPosition != start)
                continue;
            Bookmarks[i] = Bookmarks[i] with { EndPosition = end };
            return;
        }
    }

    /// <summary>
    /// Opens a hyperlink. The binary format has no link element: a hyperlink is a field
    /// whose instruction names the target and whose result is the text you see.
    /// </summary>
    public void OpenHyperlink(StoryAssembler story, Hyperlink link, RunFormat format)
    {
        if (!WriteHyperlinks)
            return;

        OpenInstruction(story, Instruction(link), format);
    }

    /// <summary>Closes a hyperlink.</summary>
    public void CloseHyperlink(StoryAssembler story, RunFormat format)
    {
        if (!WriteHyperlinks)
            return;

        CloseInstruction(story, format);
    }

    /// <summary>
    /// Opens a wrapper the binary format writes as a field. A hyperlink and a field written
    /// as one element in a package are the same thing here: the format has only the
    /// characters, so both are spelled out as them.
    /// </summary>
    public void OpenField(StoryAssembler story, InlineRange range, RunFormat format)
    {
        switch (range)
        {
            case Hyperlink link:
                OpenHyperlink(story, link, format);
                return;
            case SimpleField field:
                OpenInstruction(story, $" {field.Instruction} ", format);
                return;
        }
    }

    /// <summary>Closes a wrapper the binary format writes as a field.</summary>
    public void CloseField(StoryAssembler story, InlineRange range, RunFormat format)
    {
        switch (range)
        {
            case Hyperlink:
                CloseHyperlink(story, format);
                return;
            case SimpleField:
                CloseInstruction(story, format);
                return;
        }
    }

    private void OpenInstruction(StoryAssembler story, string instruction, RunFormat format)
    {
        story.WriteSpecial(DocChar.FieldBegin, BuildSpecialRun(format));
        Fields.Add(new FieldSpan(story.Position - 1, FieldCharKind.Begin, 0));
        story.WriteText(instruction, BuildRun(format));
        story.WriteSpecial(DocChar.FieldSeparator, BuildSpecialRun(format));
        Fields.Add(new FieldSpan(story.Position - 1, FieldCharKind.Separate, 0));
    }

    private void CloseInstruction(StoryAssembler story, RunFormat format)
    {
        story.WriteSpecial(DocChar.FieldEnd, BuildSpecialRun(format));
        Fields.Add(new FieldSpan(story.Position - 1, FieldCharKind.End, 0));
    }

    private static string Instruction(Hyperlink link)
    {
        var builder = new System.Text.StringBuilder(" HYPERLINK ");
        if (link.Url is { Length: > 0 } url)
            builder.Append('"').Append(url.Replace("\"", "'", StringComparison.Ordinal)).Append("\" ");
        if (link.Anchor is { Length: > 0 } anchor)
            builder.Append("\\l \"").Append(anchor.Replace("\"", "'", StringComparison.Ordinal)).Append("\" ");
        if (link.Tooltip is { Length: > 0 } tooltip)
            builder.Append("\\o \"").Append(tooltip.Replace("\"", "'", StringComparison.Ordinal)).Append("\" ");
        return builder.ToString();
    }

    private void WriteFieldCharacter(StoryAssembler story, FieldCharacter field, RunFormat format)
    {
        char character = field.Kind switch
        {
            FieldCharKind.Separate => DocChar.FieldSeparator,
            FieldCharKind.End => DocChar.FieldEnd,
            _ => DocChar.FieldBegin,
        };

        story.WriteSpecial(character, BuildSpecialRun(format));
        Fields.Add(new FieldSpan(story.Position - 1, field.Kind, 0));
    }
}
