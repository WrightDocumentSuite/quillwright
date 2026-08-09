using Quillwright.Diagnostics;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Doc;

/// <summary>Turns the reserved characters of the text stream into the objects they stand for.</summary>
internal static partial class DocConverter
{
    /// <summary>
    /// Appends decoded text, turning the reserved characters the legacy format uses into the
    /// objects the model represents them with.
    /// </summary>
    /// <param name="context">The file being read.</param>
    /// <param name="paragraph">Paragraph to append to.</param>
    /// <param name="text">The decoded characters.</param>
    /// <param name="run">Character formatting in force across them, and what it points at.</param>
    /// <param name="position">Character position of the first character, for resolving anchors.</param>
    private static void AppendText(
        DocReadContext context,
        Paragraph paragraph,
        string text,
        DocCharacterRun run,
        int position)
    {
        RunFormat format = run.Format;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (!IsReserved(c))
                continue;

            if (i > start)
                paragraph.AppendText(text.AsSpan(start, i - start), format);

            if (Translate(context, c, position + i, run) is { } replacement)
                paragraph.AppendObject(replacement, format);

            start = i + 1;
        }

        if (start < text.Length)
            paragraph.AppendText(text.AsSpan(start), format);
    }

    private static bool IsReserved(char value) =>
        value is '\r' or '\a' or '\v' or '\f' or '\u0001' or '\u0002' or '\u0005' or '\u0008' or '\u000E'
            or '\u0013' or '\u0014' or '\u0015';

    /// <summary>
    /// Turns one reserved character into the object it stands for, or
    /// <see langword="null"/> when it is only a structural mark with nothing to represent.
    /// </summary>
    /// <remarks>
    /// Some of these characters stand for content this converter does not carry across. That
    /// is a warning rather than a silent omission: a caller comparing the two files has to be
    /// able to find out that the shapes were not imagined away.
    /// </remarks>
    private static InlineObject? Translate(DocReadContext context, char value, int position, DocCharacterRun run) => value switch
    {
        '\v' => new Break { Kind = BreakKind.Line },
        '\f' => new Break { Kind = BreakKind.Page },
        '\u000E' => new Break { Kind = BreakKind.Column },
        '\u0001' => PictureAt(context, run.PictureOffset),
        '\u0002' => NoteAt(context, position),
        '\u0005' => CommentAt(context, position),
        '\u0008' => ShapeAt(context, position),
        '\u0013' => new FieldCharacter { Kind = FieldCharKind.Begin },
        '\u0014' => Separator(context, run),
        '\u0015' => new FieldCharacter { Kind = FieldCharKind.End },
        _ => null,
    };

    /// <summary>The picture a placeholder stands for ([MS-DOC] <c>sprmCPicLocation</c>).</summary>
    private static InlineObject? PictureAt(DocReadContext context, int offset)
    {
        if (DocPictureReader.Read(context.Data, offset, context.LoadBudget) is not { } picture)
            return Missing(
                context,
                WarningCode.UnresolvedMedia,
                "A picture was stored in a form this reader does not decode and was left out.");

        context.Images.Add(picture.Image);
        return picture;
    }

    /// <summary>
    /// The separator of a field, which for an <c>EMBED</c>, <c>LINK</c> or <c>CONTROL</c>
    /// field also stands in for the embedded object ([MS-DOC] <c>sprmCFOle2</c>).
    /// </summary>
    /// <remarks>
    /// What follows the separator is the picture Word cached of the object, and that does
    /// come across; the object itself lives in a storage of its own and does not.
    /// </remarks>
    private static InlineObject Separator(DocReadContext context, DocCharacterRun run)
    {
        if (run.IsEmbeddedObject)
            ReadEmbeddedObject(context, run.PictureOffset);

        return new FieldCharacter { Kind = FieldCharKind.Separate };
    }

    /// <summary>
    /// Registers the object a separator stands for, and says so when there is nothing in the
    /// pool to register.
    /// </summary>
    private static void ReadEmbeddedObject(DocReadContext context, int number)
    {
        if (context.Container is { } container &&
            DocObjectPool.Read(container, number, context.LoadBudget) is { } embedded)
        {
            context.EmbeddedObjects.Add(embedded);
            return;
        }

        context.Warn(
            WarningCode.UnresolvedMedia,
            "An embedded object was named but is not in the object pool, and was left out.");
    }

    /// <summary>Records what could not be converted and puts nothing in its place.</summary>
    private static InlineObject? Missing(DocReadContext context, WarningCode code, string message)
    {
        context.Warn(code, message);
        return null;
    }

    /// <summary>
    /// A note reference character in the main text points at a note; the same character
    /// inside a note's own body is the number the note prints for itself.
    /// </summary>
    private static InlineObject NoteAt(DocReadContext context, int position)
    {
        int footnote = Index(context.Footnotes.References, position);
        if (footnote >= 0)
            return new NoteReference { Id = footnote + 1, CustomMark = IsCustomMark(context.Footnotes, footnote) };

        int endnote = Index(context.Endnotes.References, position);
        return endnote >= 0
            ? new NoteReference
            {
                Id = endnote + 1,
                IsEndnote = true,
                CustomMark = IsCustomMark(context.Endnotes, endnote),
            }
            : new NoteNumberMark { IsEndnote = position >= context.Fib.MainTextLength + context.Fib.FootnoteTextLength };
    }

    /// <summary>
    /// Whether a note prints a mark the author chose rather than a number. The record against
    /// each reference says so by being zero.
    /// </summary>
    private static bool IsCustomMark(DocStoryReader notes, int index) =>
        notes.Records.ElementAtOrDefault(index) is { Length: >= 2 } record &&
        System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(record) == 0;

    /// <summary>
    /// A comment reference character appears twice: once in the main text where the comment
    /// is anchored, and again at the head of the comment's own body. Only the first is
    /// content — the second is implied by the comment existing at all.
    /// </summary>
    private static InlineObject? CommentAt(DocReadContext context, int position)
    {
        int reference = Index(context.Comments.References, position);
        return reference >= 0 ? new CommentReference { Id = reference + 1 } : null;
    }

    private static int Index(IReadOnlyList<int> positions, int position)
    {
        for (int i = 0; i < positions.Count; i++)
        {
            if (positions[i] == position)
                return i;
        }

        return -1;
    }

}
