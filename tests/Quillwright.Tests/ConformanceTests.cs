using Quillwright.Formats;
using Quillwright.IO;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Tests;

/// <summary>
/// Spot-checks against the normative text, for the rules that are easy to implement almost
/// right: the on/off datatype, how toggle properties combine, and which content type a part
/// ends up with.
/// </summary>
public class ConformanceTests
{
    /// <summary>
    /// ISO/IEC 29500-1 §22.9.2.7 restricts <c>ST_OnOff</c> to <c>xsd:boolean</c>; ECMA-376
    /// also allowed <c>on</c> and <c>off</c>, and Word still writes them. The boolean
    /// datatype collapses surrounding whitespace, so a padded value is a value.
    /// </summary>
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("on", true)]
    [InlineData("True", true)]
    [InlineData(" true ", true)]
    [InlineData("\n1", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    [InlineData("False", false)]
    [InlineData("\t off\r\n", false)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("yes", null)]
    [InlineData("2", null)]
    public void AnOnOffValue_ReadsAsTheStandardSaysItShould(string? value, bool? expected) =>
        Assert.Equal(expected, XmlHelp.ParseOnOff(value));

    /// <summary>
    /// §17.7.3: a <c>basedOn</c> chain is one layer of the hierarchy, and within a layer the
    /// first value encountered walking up from the style is the one used. Two styles in one
    /// chain both saying bold therefore means bold, not bold cancelled out.
    /// </summary>
    [Fact]
    public void ATogglePropertyStatedTwiceInOneBasedOnChain_IsNotCancelled()
    {
        WordDocument document = WordDocument.Create();
        document.Styles.Add(new Style("BoldBase", StyleKind.Paragraph) { RunFormat = RunFormat.Default with { Bold = true } });
        document.Styles.Add(new Style("BoldAgain", StyleKind.Paragraph)
        {
            BasedOn = "BoldBase",
            RunFormat = RunFormat.Default with { Bold = true },
        });

        Paragraph paragraph = document.Sections[0].AddParagraph("text");
        paragraph.Format = paragraph.Format with { StyleId = "BoldAgain" };

        Assert.True(document.Resolver.ResolveRunFormat(paragraph.Runs[0]).Bold);
    }

    /// <summary>
    /// §17.7.3: the most derived style in a chain wins outright, so turning a toggle off
    /// there is not undone by the style it is based on.
    /// </summary>
    [Fact]
    public void ATogglePropertyTurnedOffByTheDerivedStyle_StaysOff()
    {
        WordDocument document = WordDocument.Create();
        document.Styles.Add(new Style("Loud", StyleKind.Paragraph) { RunFormat = RunFormat.Default with { Bold = true } });
        document.Styles.Add(new Style("Quiet", StyleKind.Paragraph)
        {
            BasedOn = "Loud",
            RunFormat = RunFormat.Default with { Bold = false },
        });

        Paragraph paragraph = document.Sections[0].AddParagraph("text");
        paragraph.Format = paragraph.Format with { StyleId = "Quiet" };

        Assert.False(document.Resolver.ResolveRunFormat(paragraph.Runs[0]).Bold);
    }

    /// <summary>
    /// §17.7.3 again, but across layers rather than within one: a bold character style over a
    /// bold paragraph style is the case the exclusive-or exists for.
    /// </summary>
    [Fact]
    public void ATogglePropertyStatedOnTwoLayers_IsExclusiveOred()
    {
        WordDocument document = WordDocument.Create();
        document.Styles.Add(new Style("BoldBody", StyleKind.Paragraph) { RunFormat = RunFormat.Default with { Bold = true } });
        document.Styles.Add(new Style("BoldRun", StyleKind.Character) { RunFormat = RunFormat.Default with { Bold = true } });

        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.Format = paragraph.Format with { StyleId = "BoldBody" };
        paragraph.AppendText("text", RunFormat.Default with { StyleId = "BoldRun" });

        Assert.False(document.Resolver.ResolveRunFormat(paragraph.Runs[0]).Bold);
    }

    /// <summary>
    /// §17.7.3 names the toggle properties exhaustively — twelve of them — and the ones that
    /// look like they belong are not on the list. Two layers both asking for right-to-left
    /// text must give right-to-left text, not left-to-right.
    /// </summary>
    [Theory]
    [InlineData("rtl")]
    [InlineData("cs")]
    [InlineData("dstrike")]
    public void APropertyTheStandardDoesNotCallAToggle_IsNotExclusiveOred(string property)
    {
        RunFormat On(RunFormat format) => property switch
        {
            "rtl" => format with { RightToLeft = true },
            "cs" => format with { ComplexScript = true },
            _ => format with { DoubleStrike = true },
        };

        bool? Read(RunFormat format) => property switch
        {
            "rtl" => format.RightToLeft,
            "cs" => format.ComplexScript,
            _ => format.DoubleStrike,
        };

        WordDocument document = WordDocument.Create();
        document.Styles.Add(new Style("Outer", StyleKind.Paragraph) { RunFormat = On(RunFormat.Default) });
        document.Styles.Add(new Style("Inner", StyleKind.Character) { RunFormat = On(RunFormat.Default) });

        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.Format = paragraph.Format with { StyleId = "Outer" };
        paragraph.AppendText("text", RunFormat.Default with { StyleId = "Inner" });

        Assert.True(Read(document.Resolver.ResolveRunFormat(paragraph.Runs[0])));
    }

    /// <summary>
    /// The twelve that <em>are</em> toggles still cancel, which is the rule the neighbours
    /// above are so easily swept into.
    /// </summary>
    [Fact]
    public void ATogglePropertyStatedByTwoLayers_StillCancels()
    {
        WordDocument document = WordDocument.Create();
        document.Styles.Add(new Style("Outer", StyleKind.Paragraph)
        {
            RunFormat = RunFormat.Default with { Strike = true },
        });

        document.Styles.Add(new Style("Inner", StyleKind.Character)
        {
            RunFormat = RunFormat.Default with { Strike = true },
        });

        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.Format = paragraph.Format with { StyleId = "Outer" };
        paragraph.AppendText("text", RunFormat.Default with { StyleId = "Inner" });

        Assert.False(document.Resolver.ResolveRunFormat(paragraph.Runs[0]).Strike);
    }

    /// <summary>ECMA-376 part 2 §7.2.3.5(a): an override names one part and beats the defaults.</summary>
    [Fact]
    public void AnOverride_BeatsTheExtensionDefault()
    {
        var map = new ContentTypeMap();
        map.AddDefault("xml", "application/xml");
        map.AddOverride("/word/document.xml", "application/vnd.ms-word.document.main+xml");

        Assert.Equal("application/vnd.ms-word.document.main+xml", map.GetContentType("/word/document.xml"));
        Assert.Equal("application/xml", map.GetContentType("/word/settings.xml"));
    }

    /// <summary>§7.2.3.5: both comparisons are case-insensitive.</summary>
    [Fact]
    public void PartNamesAndExtensions_AreMatchedWithoutRegardToCase()
    {
        var map = new ContentTypeMap();
        map.AddDefault("XML", "application/xml");
        map.AddOverride("/word/Document.xml", "application/vnd.ms-word.document.main+xml");

        Assert.Equal("application/vnd.ms-word.document.main+xml", map.GetContentType("/WORD/DOCUMENT.XML"));
        Assert.Equal("application/xml", map.GetContentType("/word/Settings.Xml"));
    }

    /// <summary>
    /// §7.2.3.5(c)(1): the extension is what follows the last dot of the last segment. A dot
    /// in a folder name does not give the part an extension it does not have.
    /// </summary>
    [Fact]
    public void ADotInAFolderName_DoesNotBecomeTheExtension()
    {
        var map = new ContentTypeMap();
        map.AddDefault("xml", "application/xml");

        Assert.Null(map.GetContentType("/word/media.v2/logo"));
        Assert.Equal("application/xml", map.GetContentType("/word/media.v2/logo.xml"));
    }

    /// <summary>
    /// §22.9.2.15: <c>ST_UniversalMeasure</c> is a number followed by one of six unit
    /// identifiers, and <c>ST_TwipsMeasure</c> (§22.9.2.14) is a union that admits it
    /// alongside the bare number. A Strict producer writes <c>36pt</c> where a Transitional
    /// one writes <c>720</c>, and both mean the same half inch.
    /// </summary>
    [Theory]
    [InlineData("1440", 1440)]
    [InlineData("0", 0)]
    [InlineData("36pt", 720)]
    [InlineData("-36pt", -720)]
    [InlineData("5.40pt", 108)]
    [InlineData("1in", 1440)]
    [InlineData("2.54cm", 1440)]
    [InlineData("25.4mm", 1440)]
    [InlineData("6pc", 1440)]
    [InlineData("6pi", 1440)]
    public void AUniversalMeasure_ReadsAsTheLengthItNames(string value, int twips)
    {
        Assert.True(Length.TryParse(value, null, out Length length));
        Assert.Equal(twips, length.Twips);
    }

    /// <summary>
    /// The unit identifiers are the six the standard lists and nothing else, so a value that
    /// merely ends in two letters is not a measurement.
    /// </summary>
    [Theory]
    [InlineData("36px")]
    [InlineData("36em")]
    [InlineData("pt")]
    [InlineData("")]
    [InlineData("auto")]
    public void SomethingThatIsNotAMeasure_DoesNotParseAsOne(string value) =>
        Assert.False(Length.TryParse(value, null, out _));

    /// <summary>
    /// §22.9.2.9: <c>ST_HpsMeasure</c> is a union of half-points and a universal measure, so
    /// which unit the number is in depends on whether it carries one. Reading
    /// <c>12.7mm</c> as half-points would halve every font size a Strict producer wrote.
    /// </summary>
    [Theory]
    [InlineData("24", 24)]
    [InlineData("22", 22)]
    [InlineData("12.7mm", 72)]
    [InlineData("36pt", 72)]
    [InlineData("0.5in", 72)]
    public void AFontSize_IsHalfPointsUnlessItNamesAUnit(string value, int halfPoints)
    {
        Length? parsed = XmlHelp.ParseHalfPoints(value);

        Assert.NotNull(parsed);
        Assert.Equal(halfPoints, parsed.Value.HalfPoints);
    }

    /// <summary>
    /// §17.18.107: <c>ST_MeasurementOrPercent</c> is a union of three spellings of one width.
    /// Which one a producer used is readable from the value: a percentage carries its sign,
    /// a universal measure carries its unit, and a bare number is already in the unit the
    /// <c>type</c> attribute names.
    /// </summary>
    [Theory]
    [InlineData("2880", 2880)]
    [InlineData("2in", 2880)]
    [InlineData("144pt", 2880)]
    [InlineData("50%", 2500)]
    [InlineData("100%", 5000)]
    [InlineData("33.3%", 1665)]
    public void ATableWidth_ReadsInTheUnitItsTypeNames(string value, int stored) =>
        Assert.Equal(stored, SharedFormatReader.ParseWidth(value));
}
