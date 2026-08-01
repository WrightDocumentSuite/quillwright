using System.Globalization;
using Quillwright.Editing;
using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// Recomputing field results (ISO/IEC 29500-1 §17.16): the formula grammar of §17.16.3, the
/// formatting switches of §17.16.4, and the promise that a field this cannot work out is left
/// alone rather than guessed at.
/// </summary>
public class FieldEvaluationTests
{
    private static readonly FieldUpdateOptions Invariant = new() { Culture = CultureInfo.InvariantCulture };

    /// <summary>The worked example of §17.16.3.3, bookmarks and all.</summary>
    [Fact]
    public void TheSpecificationsWorkedExample_ComesOutAtTwentyOneAndAHalf()
    {
        WordDocument document = WordDocument.Create();
        Bookmark(document, "X", "4");
        Bookmark(document, "Y", "2");

        Assert.Equal("21.5", Updated(document, "=((-1 + X^2) * 3 - Y)/2"));
    }

    [Theory]
    [InlineData("=1/3", "0.3333333333")]
    [InlineData("=2+3*4", "14")]
    [InlineData("=(2+3)*4", "20")]
    [InlineData("=-2^2", "4")]
    [InlineData("=10>3", "1")]
    [InlineData("=10<3", "0")]
    [InlineData("=3<>3", "0")]
    [InlineData("=ABS(-7)", "7")]
    [InlineData("=INT(7.9)", "7")]
    [InlineData("=SIGN(-3)", "-1")]
    [InlineData("=ROUND(3.14159,2)", "3.14")]
    [InlineData("=ROUND(1234,-2)", "1200")]
    [InlineData("=MAX(1,7,3)", "7")]
    [InlineData("=MIN(1,7,3)", "1")]
    [InlineData("=COUNT(1,7,3)", "3")]
    [InlineData("=SUM(1,7,3)", "11")]
    [InlineData("=PRODUCT(2,3,4)", "24")]
    [InlineData("=AVERAGE(2,4,6)", "4")]
    [InlineData("=AND(1,0)", "0")]
    [InlineData("=OR(1,0)", "1")]
    [InlineData("=NOT(0)", "1")]
    [InlineData("=TRUE", "1")]
    [InlineData("=FALSE", "0")]
    [InlineData("=DEFINED(1+1)", "1")]
    [InlineData("=DEFINED(NoSuchName)", "0")]
    public void AFormula_EvaluatesToTheValueTheGrammarGivesIt(string instruction, string expected) =>
        Assert.Equal(expected, Updated(WordDocument.Create(), instruction));

    /// <summary>The four cases §17.16.3.4 spells out: the remainder keeps the sign of the dividend.</summary>
    [Theory]
    [InlineData("=MOD(21,5)", "1")]
    [InlineData("=MOD(21,-5)", "1")]
    [InlineData("=MOD(-21,5)", "-1")]
    [InlineData("=MOD(-21,-5)", "-1")]
    public void Modulo_KeepsTheSignOfTheDividend(string instruction, string expected) =>
        Assert.Equal(expected, Updated(WordDocument.Create(), instruction));

    /// <summary>The numeric picture examples of §17.16.4.2.</summary>
    [Theory]
    [InlineData("=4+5 \\# 00.00", "09.00")]
    [InlineData("=9+6 \\# $###", "$ 15")]
    [InlineData("=111053+111439 \\# x##", "492")]
    [InlineData("=95.4 \\# $###.00", "$ 95.40")]
    [InlineData("=2456800 \\# $#,###,###", "$2,456,800")]
    [InlineData("=80-90 \\# -##", "-10")]
    [InlineData("=90-80 \\# +##", "+10")]
    [InlineData("=33 \\# ##%", "33%")]
    [InlineData("=1/8 \\# 0.00x", "0.125")]
    public void ANumericPicture_LaysTheDigitsIntoIt(string instruction, string expected) =>
        Assert.Equal(expected, Updated(WordDocument.Create(), instruction));

    [Fact]
    public void APictureWithASecondForm_UsesItForANegativeResult() =>
        Assert.Equal("($1,250.50)", Updated(WordDocument.Create(), "=0-1250.5 \\# \"$#,##0.00;($#,##0.00)\""));

    [Theory]
    [InlineData("ROMAN", "XIV")]
    [InlineData("roman", "xiv")]
    [InlineData("ALPHABETIC", "N")]
    [InlineData("Ordinal", "14th")]
    [InlineData("Arabic", "14")]
    [InlineData("Hex", "E")]
    public void AGeneralSwitch_RenumbersANumericResult(string argument, string expected) =>
        Assert.Equal(expected, Updated(WordDocument.Create(), $"=7*2 \\* {argument}"));

    [Theory]
    [InlineData("Upper", "MARY SMITH")]
    [InlineData("Lower", "mary smith")]
    [InlineData("Caps", "Mary Smith")]
    [InlineData("FirstCap", "Mary smith")]
    public void AGeneralSwitch_RecasesATextResult(string argument, string expected)
    {
        WordDocument document = WordDocument.Create();
        document.Properties.Creator = "mary smith";

        Assert.Equal(expected, Updated(document, $"AUTHOR \\* {argument}"));
    }

    [Fact]
    public void ADateField_UsesThePictureItWasGiven()
    {
        var options = new FieldUpdateOptions
        {
            Culture = CultureInfo.GetCultureInfo("en-US"),
            Now = new DateTime(2005, 12, 31, 0, 0, 0, DateTimeKind.Unspecified),
        };

        Assert.Equal(
            "Saturday, December 31, 2005",
            Updated(WordDocument.Create(), "DATE \\@ \"dddd, MMMM dd, yyyy\"", options));
    }

    [Fact]
    public void ATimeField_SpellsOutTheMeridianTheWayWordDoes()
    {
        var options = new FieldUpdateOptions
        {
            Culture = CultureInfo.GetCultureInfo("en-US"),
            Now = new DateTime(2005, 12, 31, 14, 5, 0, DateTimeKind.Unspecified),
        };

        Assert.Equal("2:05 PM", Updated(WordDocument.Create(), "TIME \\@ \"h:mm AM/PM\"", options));
    }

    [Fact]
    public void ADocumentPropertyField_ReadsTheProperty()
    {
        WordDocument document = WordDocument.Create();
        document.CustomProperties.Set("Project Number", PropertyValue.FromText("QW-42"));

        Assert.Equal("QW-42", Updated(document, "DOCPROPERTY \"Project Number\""));
    }

    [Fact]
    public void ADocumentPropertyField_AlsoReadsTheBuiltInCategories()
    {
        WordDocument document = WordDocument.Create();
        document.ApplicationProperties.Company = "Quillwright";

        Assert.Equal("Quillwright", Updated(document, "DOCPROPERTY Company"));
    }

    [Fact]
    public void ACreationDateField_ReadsTheDocumentsOwnDate()
    {
        WordDocument document = WordDocument.Create();
        document.Properties.Created = new DateTimeOffset(2021, 3, 4, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal("2021-03-04", Updated(document, "CREATEDATE \\@ \"yyyy-MM-dd\""));
    }

    [Fact]
    public void AConditionalField_ChoosesBetweenItsTwoResults()
    {
        WordDocument document = WordDocument.Create();
        Bookmark(document, "Total", "120");

        Assert.Equal("over budget", Updated(document, "IF Total > 100 \"over budget\" \"within budget\""));
    }

    [Fact]
    public void AReferenceField_ReadsTheBookmarkItNames()
    {
        WordDocument document = WordDocument.Create();
        Bookmark(document, "Customer", "Acme");

        Assert.Equal("Acme", Updated(document, "REF Customer"));
    }

    /// <summary>A total over a column of a table, which is what a formula field is mostly for.</summary>
    [Fact]
    public void AFormulaInATableCell_AddsUpTheCellsAboveIt()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        foreach (string amount in new[] { "10", "20", "12.5" })
            table.Rows.Add(Row(amount));

        TableRow total = Row(string.Empty);
        table.Rows.Add(total);
        document.Sections[0].Blocks.Add(table);

        Paragraph paragraph = total.Cells[0].Blocks.Paragraphs.First();
        paragraph.AppendField("=SUM(ABOVE)", "0");

        Assert.Equal(1, paragraph.UpdateFields(Invariant));
        Assert.Equal("42.5", paragraph.Fields().Single().Result);
    }

    [Fact]
    public void AFormulaInATableCell_ReadsCellsByName()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        table.Rows.Add(Row("5", "7"));
        table.Rows.Add(Row(string.Empty, string.Empty));
        document.Sections[0].Blocks.Add(table);

        Paragraph paragraph = table.Rows[1].Cells[0].Blocks.Paragraphs.First();
        paragraph.AppendField("=A1+B1", "0");

        Assert.Equal(1, paragraph.UpdateFields(Invariant));
        Assert.Equal("12", paragraph.Fields().Single().Result);
    }

    /// <summary>
    /// §17.16.3.5 names a cell after the column it sits in, and columns are the table's grid
    /// columns. A cell spanning two of them takes both names, so the cell after it starts at
    /// the third — <c>C1</c>, not <c>B1</c>.
    /// </summary>
    [Fact]
    public void AFormulaInATableCell_NamesCellsByGridColumnAcrossAMerge()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        TableRow header = Row("100", "7");
        header.Cells[0].Format = header.Cells[0].Format with { GridSpan = 2 };
        table.Rows.Add(header);
        table.Rows.Add(Row(string.Empty, string.Empty, string.Empty));
        document.Sections[0].Blocks.Add(table);

        Paragraph paragraph = table.Rows[1].Cells[0].Blocks.Paragraphs.First();
        paragraph.AppendField("=A1+C1", "0");

        Assert.Equal(1, paragraph.UpdateFields(Invariant));
        Assert.Equal("107", paragraph.Fields().Single().Result);
    }

    /// <summary>
    /// The same cell must not be counted once per grid column it covers, which is what makes
    /// a total over a merged row come out double.
    /// </summary>
    [Fact]
    public void AFormulaOverARowWithAMerge_CountsTheSpanningCellOnce()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        TableRow row = Row("10", "5", string.Empty);
        row.Cells[0].Format = row.Cells[0].Format with { GridSpan = 2 };
        table.Rows.Add(row);
        document.Sections[0].Blocks.Add(table);

        Paragraph paragraph = row.Cells[2].Blocks.Paragraphs.First();
        paragraph.AppendField("=SUM(LEFT)", "0");

        Assert.Equal(1, paragraph.UpdateFields(Invariant));
        Assert.Equal("15", paragraph.Fields().Single().Result);
    }

    /// <summary>
    /// A field whose value depends on where the text lands on a page cannot be computed
    /// without a layout, so it keeps what it had and asks the consumer to redo it.
    /// </summary>
    [Fact]
    public void APageField_IsLeftAloneAndMarkedDirty()
    {
        Paragraph paragraph = Paragraph(WordDocument.Create(), "PAGE", result: "7");

        Assert.False(paragraph.Fields().Single().Update(Invariant));
        Assert.Equal("7", paragraph.Fields().Single().Result);
        Assert.True(Begins(paragraph).Dirty);
    }

    [Fact]
    public void AnUnknownField_IsAlsoLeftAlone()
    {
        Paragraph paragraph = Paragraph(WordDocument.Create(), "ADDRESSBLOCK \\f \"<<_TITLE0_>>\"", result: "someone");

        Assert.Equal(0, paragraph.UpdateFields(Invariant));
        Assert.Equal("someone", paragraph.Fields().Single().Result);
        Assert.True(Begins(paragraph).Dirty);
    }

    [Fact]
    public void AnUpdatedField_IsNoLongerDirty()
    {
        Paragraph paragraph = Paragraph(WordDocument.Create(), "=1+1");
        Begins(paragraph).Dirty = true;

        Assert.Equal(1, paragraph.UpdateFields(Invariant));
        Assert.False(Begins(paragraph).Dirty);
    }

    [Fact]
    public void UpdatingTheDocument_CountsTheFieldsItRecomputed()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendField("=1+1", "0");
        paragraph.AppendField("PAGE", "1");
        paragraph.AppendField("=200*3", "0");

        Assert.Equal(2, document.UpdateFields(Invariant));
        Assert.Equal(["2", "1", "600"], paragraph.Fields().Select(static f => f.Result));
    }

    /// <summary>
    /// A sequence numbers the captions of one series, and what it counts is nowhere in the
    /// file: the number is the field's position among the ones naming the same series.
    /// </summary>
    [Fact]
    public void ASequenceField_CountsThePrecedingOnesOfItsOwnSeries()
    {
        WordDocument document = WordDocument.Create();
        foreach (string _ in new[] { "a", "b", "c" })
            document.Sections[0].AddParagraph().AppendField("SEQ Figure", "0");

        document.Sections[0].AddParagraph().AppendField("SEQ Table", "0");

        Assert.Equal(4, document.UpdateFields(Invariant));
        Assert.Equal(["1", "2", "3", "1"], document.Fields().Select(static f => f.Result));
    }

    [Fact]
    public void ASequenceField_RestartsAndRepeatsWhenToldTo()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendField("SEQ Figure", "0");
        paragraph.AppendField("SEQ Figure", "0");
        paragraph.AppendField("SEQ Figure \\c", "0");
        paragraph.AppendField("SEQ Figure \\r 7", "0");
        paragraph.AppendField("SEQ Figure", "0");

        paragraph.UpdateFields(Invariant);

        Assert.Equal(["1", "2", "2", "7", "8"], paragraph.Fields().Select(static f => f.Result));
    }

    [Fact]
    public void ASequenceField_TakesTheGeneralSwitchLikeAnyNumber()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendField("SEQ Figure", "0");
        paragraph.AppendField("SEQ Figure \\* ROMAN", "0");

        paragraph.UpdateFields(Invariant);

        Assert.Equal(["1", "II"], paragraph.Fields().Select(static f => f.Result));
    }

    /// <summary>A number that restarts at each heading needs numbering this does not compute.</summary>
    [Fact]
    public void ASequenceFieldNumberedByHeading_IsLeftAlone()
    {
        Paragraph paragraph = Paragraph(WordDocument.Create(), "SEQ Figure \\s 1", result: "1-1");

        Assert.Equal(0, paragraph.UpdateFields(Invariant));
        Assert.Equal("1-1", paragraph.Fields().Single().Result);
    }

    [Fact]
    public void AStyleReferenceField_QuotesTheNearestParagraphAbove()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Chapter One", "Heading1");
        document.Sections[0].AddParagraph("Some prose.");
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendField("STYLEREF \"Heading1\"", "?");

        Assert.Equal(1, paragraph.UpdateFields(Invariant));
        Assert.Equal("Chapter One", paragraph.Fields().Single().Result);
    }

    /// <summary>With nothing above it, the reference looks forward instead, as Word does.</summary>
    [Fact]
    public void AStyleReferenceField_LooksForwardWhenNothingIsAbove()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendField("STYLEREF Heading1", "?");
        document.Sections[0].AddParagraph("Chapter Two", "Heading1");

        Assert.Equal(1, paragraph.UpdateFields(Invariant));
        Assert.Equal("Chapter Two", paragraph.Fields().Single().Result);
    }

    /// <summary>Asking for the last one on the page is asking for a layout.</summary>
    [Fact]
    public void AStyleReferenceAskingForThePage_IsLeftAlone()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Chapter One", "Heading1");
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendField("STYLEREF Heading1 \\l", "?");

        Assert.Equal(0, paragraph.UpdateFields(Invariant));
        Assert.Equal("?", paragraph.Fields().Single().Result);
    }

    [Fact]
    public void ADocumentVariableField_ReadsTheVariable()
    {
        WordDocument document = WordDocument.Create();
        document.Settings.Variables["Region"] = "North";

        Assert.Equal("North", Updated(document, "DOCVARIABLE Region"));
    }

    [Fact]
    public async Task DocumentVariables_SurviveARoundTrip()
    {
        WordDocument document = WordDocument.Create();
        document.Settings.Variables["Region"] = "North";
        document.Settings.Variables["Client"] = "Acme & Co";

        WordDocument reopened = await DocumentFixture.RoundTripAsync(document, "document variables");

        Assert.Equal("North", reopened.Settings.Variables["Region"]);
        Assert.Equal("Acme & Co", reopened.Settings.Variables["Client"]);
        Assert.Equal(2, reopened.Settings.Variables.Count);
    }

    [Fact]
    public void ADocumentVariable_CanBeRemoved()
    {
        WordDocument document = WordDocument.Create();
        document.Settings.Variables["Region"] = "North";

        Assert.True(document.Settings.Variables.Remove("region"));
        Assert.Empty(document.Settings.Variables);
        Assert.Null(document.Settings.GetRaw("docVars"));
    }

    [Fact]
    public void AUserNameField_ComesFromTheOptions()
    {
        var options = new FieldUpdateOptions
        {
            Culture = CultureInfo.InvariantCulture,
            UserName = "Ada Lovelace",
            UserInitials = "AL",
        };

        Assert.Equal("Ada Lovelace", Updated(WordDocument.Create(), "USERNAME", options));
        Assert.Equal("AL", Updated(WordDocument.Create(), "USERINITIALS", options));
    }

    [Fact]
    public async Task AFieldSurvivesTheRoundTripWithItsNewResult()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendField("=6*7", "0");
        document.UpdateFields(Invariant);

        WordDocument reopened = await DocumentFixture.RoundTripAsync(document);

        Assert.Equal("42", reopened.Fields().Single().Result);
    }

    /// <summary>
    /// Updates the one field of a fresh paragraph and reads the result back. The field has to
    /// be looked up again because writing a result of a different length moves the offsets
    /// the old view was made of.
    /// </summary>
    private static string Updated(WordDocument document, string instruction, FieldUpdateOptions? options = null)
    {
        Paragraph paragraph = Paragraph(document, instruction);
        Assert.Equal(1, paragraph.UpdateFields(options ?? Invariant));
        return paragraph.Fields().Single().Result;
    }

    private static Paragraph Paragraph(WordDocument document, string instruction, string result = "0")
    {
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendField(instruction, result);
        return paragraph;
    }

    private static TableRow Row(params string[] cells)
    {
        var row = new TableRow();
        foreach (string cell in cells)
            row.AddCell(cell);
        return row;
    }

    private static FieldCharacter Begins(Paragraph paragraph) =>
        (FieldCharacter)paragraph.ObjectAt(paragraph.Fields().Single().BeginOffset)!;

    private static void Bookmark(WordDocument document, string name, string text)
    {
        Paragraph paragraph = document.Sections[0].AddParagraph();
        int id = document.Sections[0].Blocks.Count;
        paragraph.AddMark(new BookmarkStart { Id = id, Name = name }, 0);
        paragraph.AppendText(text);
        paragraph.AddMark(new BookmarkEnd { Id = id }, paragraph.Text.Length);
    }
}
