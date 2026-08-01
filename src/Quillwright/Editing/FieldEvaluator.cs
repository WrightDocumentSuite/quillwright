using System.Globalization;
using Quillwright.Model;

namespace Quillwright.Editing;

/// <summary>What a field update is allowed to assume about the world around it.</summary>
public sealed class FieldUpdateOptions
{
    /// <summary>Shared instance with default settings.</summary>
    public static FieldUpdateOptions Default { get; } = new();

    /// <summary>
    /// The moment <c>DATE</c> and <c>TIME</c> report, or <see langword="null"/> for the
    /// current one. Setting it makes an update reproducible.
    /// </summary>
    public DateTime? Now { get; init; }

    /// <summary>Culture the results are formatted in. Defaults to the current one.</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.CurrentCulture;

    /// <summary>Name reported by a <c>FILENAME</c> field, which the document does not know.</summary>
    public string? FileName { get; init; }

    /// <summary>Name reported by a <c>USERNAME</c> field, which belongs to the application.</summary>
    public string? UserName { get; init; }

    /// <summary>Initials reported by a <c>USERINITIALS</c> field.</summary>
    public string? UserInitials { get; init; }

    /// <summary>Address reported by a <c>USERADDRESS</c> field.</summary>
    public string? UserAddress { get; init; }
}

/// <summary>
/// Recomputes the cached results of fields (ISO/IEC 29500-1 §17.16).
/// </summary>
/// <remarks>
/// <para>
/// A field has two halves: an instruction and the result an application last computed for it.
/// Nothing in the file format says the two agree — the result is a cache, and a consumer is
/// free to refresh it whenever it likes. This refreshes the fields whose value follows from
/// the document alone.
/// </para>
/// <para>
/// A field whose value depends on where the text falls on a page — <c>PAGE</c>, <c>PAGEREF</c>,
/// a table of contents — cannot be computed without laying the document out, which this
/// library does not do. Those are left with the result they arrived with and marked dirty, so
/// the consumer that does have a layout engine recomputes them when the document is opened.
/// </para>
/// </remarks>
public static class FieldEvaluator
{
    /// <summary>
    /// Recomputes every field of a document whose value follows from the document itself.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="options">What the update may assume; defaults apply when omitted.</param>
    /// <returns>How many fields were given a new result.</returns>
    public static int UpdateFields(this WordDocument document, FieldUpdateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        int updated = 0;
        foreach (Paragraph paragraph in document.AllContainers.SelectMany(static c => c.Blocks.Paragraphs).ToList())
            updated += UpdateFields(paragraph, options);

        return updated;
    }

    /// <summary>Recomputes the fields of one paragraph.</summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="options">What the update may assume; defaults apply when omitted.</param>
    /// <returns>How many fields were given a new result.</returns>
    public static int UpdateFields(this Paragraph paragraph, FieldUpdateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        // A new result is rarely the same length as the old one, which moves every field
        // after it in the paragraph. The list is therefore taken again for each field rather
        // than once, because the offsets in the old one no longer point where they did.
        int updated = 0;
        for (int i = 0; ; i++)
        {
            List<Field> fields = [.. paragraph.Fields()];
            if (i >= fields.Count)
                return updated;

            if (fields[i].Update(options))
                updated++;
        }
    }

    /// <summary>
    /// Recomputes the cached result of one field.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="options">What the update may assume; defaults apply when omitted.</param>
    /// <returns>
    /// <see langword="true"/> when the field was given a new result; <see langword="false"/>
    /// when it needs a layout this library does not have, or is not one this evaluates, in
    /// which case it is left as it was and marked dirty.
    /// </returns>
    /// <remarks>
    /// A <see cref="Field"/> is a view onto offsets in a paragraph, so writing a result of a
    /// different length leaves this one and every field after it pointing at the wrong place.
    /// Read the field again from <see cref="FieldExtensions.Fields(Paragraph)"/> afterwards,
    /// or use <see cref="UpdateFields(Paragraph, FieldUpdateOptions)"/>, which does it for you.
    /// </remarks>
    public static bool Update(this Field field, FieldUpdateOptions? options = null)
    {
        FieldInstruction instruction = FieldInstruction.Parse(field.Instruction);
        if (field.HasResult && Evaluate(instruction, field, options ?? FieldUpdateOptions.Default) is { } result)
        {
            field.SetResult(result);
            field.IsDirty = false;
            return true;
        }

        field.IsDirty = true;
        return false;
    }

    /// <summary>
    /// What a field's instruction evaluates to, or <see langword="null"/> when this cannot
    /// say — which is not the same as an empty result.
    /// </summary>
    /// <param name="instruction">The parsed instruction.</param>
    /// <param name="field">The field it belongs to, for the document and the table around it.</param>
    /// <param name="options">What the update may assume.</param>
    public static string? Evaluate(FieldInstruction instruction, Field field, FieldUpdateOptions options)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(options);

        WordDocument? document = field.Paragraph.Document;
        return instruction.Name switch
        {
            "=" => Formula(instruction, field, options),
            "DATE" or "TIME" => Moment(instruction, options.Now ?? DateTime.Now, instruction.Name == "TIME", options),
            "CREATEDATE" => Stamp(instruction, document?.Properties.Created, options),
            "SAVEDATE" => Stamp(instruction, document?.Properties.Modified, options),
            "AUTHOR" => Text(instruction, Argument(instruction, 0) ?? document?.Properties.Creator, options),
            "TITLE" => Text(instruction, Argument(instruction, 0) ?? document?.Properties.Title, options),
            "SUBJECT" => Text(instruction, Argument(instruction, 0) ?? document?.Properties.Subject, options),
            "KEYWORDS" => Text(instruction, Argument(instruction, 0) ?? document?.Properties.Keywords, options),
            "COMMENTS" => Text(instruction, Argument(instruction, 0) ?? document?.Properties.Description, options),
            "LASTSAVEDBY" => Text(instruction, document?.Properties.LastModifiedBy, options),
            "FILENAME" => Text(instruction, options.FileName, options),
            "DOCPROPERTY" => DocumentProperty(instruction, document, options),
            "NUMWORDS" => Count(instruction, document?.ApplicationProperties.Words, options),
            "NUMCHARS" => Count(instruction, document?.ApplicationProperties.Characters, options),
            "NUMPAGES" => Count(instruction, document?.ApplicationProperties.Pages, options),
            "IF" => Conditional(instruction, field, options),
            "QUOTE" => Text(instruction, Argument(instruction, 0), options),
            "SET" => Assign(instruction),
            "SEQ" => Sequence(instruction, field, options),
            "STYLEREF" => Reference(instruction, field, options),
            "DOCVARIABLE" => Text(instruction, Variable(instruction, document), options),
            "USERNAME" => Text(instruction, Argument(instruction, 0) ?? options.UserName, options),
            "USERINITIALS" => Text(instruction, Argument(instruction, 0) ?? options.UserInitials, options),
            "USERADDRESS" => Text(instruction, Argument(instruction, 0) ?? options.UserAddress, options),
            "REF" or "" => Bookmark(instruction, field, options),
            _ => null,
        };
    }

    private static string? Variable(FieldInstruction instruction, WordDocument? document) =>
        document is null || Argument(instruction, 0) is not { } name ? null : document.Settings.Variables[name];

    private static string? Formula(FieldInstruction instruction, Field field, FieldUpdateOptions options)
    {
        if (Argument(instruction, 0) is not { } expression ||
            FieldFormula.Evaluate(expression, new FieldNames(field)) is not { } value)
            return null;

        string text = instruction.NumericPicture is { } picture
            ? FieldFormat.Numeric(value, picture, options.Culture)
            : FieldFormat.Number(value, options.Culture);

        return FieldFormat.General(text, value, instruction.GeneralFormat, options.Culture);
    }

    private static string Moment(FieldInstruction instruction, DateTime now, bool isTime, FieldUpdateOptions options)
    {
        if (instruction.DatePicture is { } picture)
            return now.ToString(FieldFormat.DatePattern(picture), options.Culture);

        return isTime ? now.ToString("t", options.Culture) : now.ToString("d", options.Culture);
    }

    private static string? Stamp(FieldInstruction instruction, DateTimeOffset? moment, FieldUpdateOptions options) =>
        moment is { } value ? Moment(instruction, value.LocalDateTime, isTime: false, options) : null;

    private static string? Text(FieldInstruction instruction, string? value, FieldUpdateOptions options) =>
        value is null ? null : FieldFormat.General(value, Numeric(value), instruction.GeneralFormat, options.Culture);

    private static string? Count(FieldInstruction instruction, int? value, FieldUpdateOptions options)
    {
        if (value is not { } count)
            return null;

        string text = instruction.NumericPicture is { } picture
            ? FieldFormat.Numeric(count, picture, options.Culture)
            : count.ToString(options.Culture);

        return FieldFormat.General(text, count, instruction.GeneralFormat, options.Culture);
    }

    private static string? DocumentProperty(FieldInstruction instruction, WordDocument? document, FieldUpdateOptions options)
    {
        if (document is null || Argument(instruction, 0) is not { } name)
            return null;

        string? value = BuiltInProperty(document, name) ?? document.CustomProperties[name]?.Value.ToString();
        return value is null ? null : FieldFormat.General(value, Numeric(value), instruction.GeneralFormat, options.Culture);
    }

    /// <summary>The properties a <c>DOCPROPERTY</c> field can name by the categories of §17.16.1.</summary>
    private static string? BuiltInProperty(WordDocument document, string name) => name.ToUpperInvariant() switch
    {
        "AUTHOR" => document.Properties.Creator,
        "CATEGORY" => document.Properties.Category,
        "COMMENTS" => document.Properties.Description,
        "COMPANY" => document.ApplicationProperties.Company,
        "KEYWORDS" => document.Properties.Keywords,
        "LASTSAVEDBY" => document.Properties.LastModifiedBy,
        "MANAGER" => document.ApplicationProperties.Manager,
        "NAMEOFAPPLICATION" => document.ApplicationProperties.Application,
        "REVISIONNUMBER" => document.Properties.Revision,
        "SUBJECT" => document.Properties.Subject,
        "TEMPLATE" => document.ApplicationProperties.Template,
        "TITLE" => document.Properties.Title,
        "HYPERLINKBASE" => document.ApplicationProperties.HyperlinkBase,
        "CHARACTERS" => document.ApplicationProperties.Characters?.ToString(CultureInfo.InvariantCulture),
        "CHARACTERSWITHSPACES" => document.ApplicationProperties.CharactersWithSpaces?.ToString(CultureInfo.InvariantCulture),
        "LINES" => document.ApplicationProperties.Lines?.ToString(CultureInfo.InvariantCulture),
        "PAGES" => document.ApplicationProperties.Pages?.ToString(CultureInfo.InvariantCulture),
        "PARAGRAPHS" => document.ApplicationProperties.Paragraphs?.ToString(CultureInfo.InvariantCulture),
        "WORDS" => document.ApplicationProperties.Words?.ToString(CultureInfo.InvariantCulture),
        "TOTALEDITINGTIME" => document.ApplicationProperties.TotalEditingMinutes?.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>
    /// An <c>IF</c> field, whose instruction is a comparison and the two results to choose
    /// between. Both sides are compared as numbers when both are numbers, and as text
    /// otherwise, which is what Word does.
    /// </summary>
    private static string? Conditional(FieldInstruction instruction, Field field, FieldUpdateOptions options)
    {
        if (instruction.Arguments.Count < 4)
            return null;

        var names = new FieldNames(field);
        string left = names.Expand(instruction.Arguments[0]);
        string right = names.Expand(instruction.Arguments[2]);
        if (Compare(left, right, instruction.Arguments[1]) is not { } holds)
            return null;

        string chosen = holds ? instruction.Arguments[3] : instruction.Arguments.Count > 4 ? instruction.Arguments[4] : string.Empty;
        return FieldFormat.General(chosen, Numeric(chosen), instruction.GeneralFormat, options.Culture);
    }

    private static bool? Compare(string left, string right, string comparison)
    {
        int order = Numeric(left) is { } x && Numeric(right) is { } y
            ? x.CompareTo(y)
            : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);

        return comparison switch
        {
            "=" => order == 0,
            "<>" or "!=" => order != 0,
            "<" => order < 0,
            "<=" => order <= 0,
            ">" => order > 0,
            ">=" => order >= 0,
            _ => null,
        };
    }

    /// <summary>
    /// A <c>SET</c> field names a bookmark and gives it a value. The bookmark is where the
    /// value lives, so there is nothing to show; the field's own result is empty.
    /// </summary>
    private static string? Assign(FieldInstruction instruction) =>
        instruction.Arguments.Count >= 2 ? string.Empty : null;

    /// <summary>
    /// A <c>REF</c> field, or the bare bookmark name that means the same thing (§17.16.5.51).
    /// A reference asking for a page number needs a layout and is not evaluated.
    /// </summary>
    private static string? Bookmark(FieldInstruction instruction, Field field, FieldUpdateOptions options)
    {
        string? name = instruction.Name == "REF" ? Argument(instruction, 0) : instruction.Name;
        if (string.IsNullOrEmpty(name) || instruction.Has("p") || instruction.Has("n"))
            return null;

        string? value = new FieldNames(field).Resolve(name);
        return value is null ? null : FieldFormat.General(value, Numeric(value), instruction.GeneralFormat, options.Culture);
    }

    private static string? Argument(FieldInstruction instruction, int index) =>
        index < instruction.Arguments.Count ? instruction.Arguments[index] : null;

    private static double? Numeric(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;

    /// <summary>
    /// A <c>SEQ</c> field, which numbers the captions, figures or tables of one series. What
    /// it counts is not stored anywhere: the number is the position of this field among the
    /// ones naming the same series, so working it out means walking the document.
    /// </summary>
    private static string? Sequence(FieldInstruction instruction, Field field, FieldUpdateOptions options)
    {
        // A number that restarts at each heading needs the heading numbering, which needs a
        // numbering pass this does not make.
        if (Argument(instruction, 0) is not { } identifier || instruction.Has("s"))
            return null;

        if (field.Paragraph.Document is not { } document)
            return null;

        int value = 0;
        foreach (Field other in document.Fields())
        {
            FieldInstruction spelling = other.Name == "SEQ" ? FieldInstruction.Parse(other.Instruction) : NoField;
            bool counts = spelling.Name == "SEQ" &&
                string.Equals(Argument(spelling, 0), identifier, StringComparison.OrdinalIgnoreCase);

            if (counts)
                value = Advance(spelling, value);

            if (other.Paragraph == field.Paragraph && other.BeginOffset == field.BeginOffset)
                break;
        }

        if (instruction.Has("h"))
            return string.Empty;

        return FieldFormat.General(
            value.ToString(options.Culture), value, instruction.GeneralFormat ?? "Arabic", options.Culture);
    }

    /// <summary>What one entry in a sequence does to the count: reset it, repeat it, or add one.</summary>
    private static int Advance(FieldInstruction instruction, int value)
    {
        if (instruction.Argument("r") is { } reset &&
            int.TryParse(reset, NumberStyles.Integer, CultureInfo.InvariantCulture, out int at))
        {
            return at;
        }

        return instruction.Has("c") ? value : value + 1;
    }

    /// <summary>
    /// A <c>STYLEREF</c> field, which quotes the nearest paragraph carrying a named style —
    /// the chapter title above a figure, or the entry above a page of a glossary.
    /// </summary>
    /// <remarks>
    /// Word answers it with the nearest such paragraph <em>on the page</em>, which is why the
    /// field is mostly used in headers and why one in a header cannot be answered here at
    /// all. In the body the nearest one in the text is the same paragraph, so that is what
    /// this reads; a switch asking for a number or for the last one on the page is not.
    /// </remarks>
    private static string? Reference(FieldInstruction instruction, Field field, FieldUpdateOptions options)
    {
        if (Argument(instruction, 0) is not { } style ||
            instruction.Has("l") || instruction.Has("n") || instruction.Has("p") ||
            instruction.Has("r") || instruction.Has("w") ||
            field.Paragraph.Document is not { } document)
        {
            return null;
        }

        Paragraph[] body = [.. document.Paragraphs];
        int here = Array.IndexOf(body, field.Paragraph);
        if (here < 0)
            return null;

        for (int i = here; i >= 0; i--)
        {
            if (Styled(document, body[i], style))
                return Text(instruction, body[i].GetText(), options);
        }

        for (int i = here + 1; i < body.Length; i++)
        {
            if (Styled(document, body[i], style))
                return Text(instruction, body[i].GetText(), options);
        }

        return null;
    }

    /// <summary>Whether a paragraph carries a style, named either by its identifier or by its name.</summary>
    private static bool Styled(WordDocument document, Paragraph paragraph, string style)
    {
        if (paragraph.Format.StyleId is not { } id)
            return false;

        return string.Equals(id, style, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(document.Styles.Find(id)?.Name, style, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An instruction that names no field, for comparing against one that does.</summary>
    private static readonly FieldInstruction NoField = FieldInstruction.Parse(string.Empty);
}
