using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Tests;

/// <summary>
/// The deviations from ISO/IEC 29500-1 §17 the coverage sweep found, one test each so that
/// none of them can come back.
/// </summary>
/// <remarks>
/// The sweep itself is written up in <c>docs/wordprocessingml-coverage.md</c>. What is checked
/// here is the handful of places where a modelled part used to drop markup it did not
/// understand instead of carrying it through.
/// </remarks>
public class WordprocessingMlCoverageTests
{
    /// <summary>
    /// §17.4.61: a row pasted from a table with different borders carries its own overrides,
    /// and the schema requires them before everything else in the row.
    /// </summary>
    [Fact]
    public async Task ARowsOverridesOfTheTableFormatting_SurviveTheRoundTrip()
    {
        WordDocument document = OneCellTable(out Table table);
        table.Rows[0].PropertyExceptionsXml =
            "<w:tblPrEx><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"FF0000\"/></w:tblBorders></w:tblPrEx>";

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "a row with its own borders");

        Assert.Contains(
            "FF0000",
            reloaded.Blocks.OfType<Table>().Single().Rows[0].PropertyExceptionsXml,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// §17.4.78: a table's content rows may be interleaved with run-level marks. A bookmark
    /// that opens between two rows used to be dropped, which left its closing mark dangling.
    /// </summary>
    [Fact]
    public async Task AMarkBetweenTableRows_SurvivesTheRoundTrip()
    {
        WordDocument document = OneCellTable(out Table table);
        table.PreservedXml = "<w:bookmarkStart w:id=\"7\" w:name=\"spanning\"/><w:bookmarkEnd w:id=\"7\"/>";

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "a bookmark between rows");

        Assert.Contains("spanning", reloaded.Blocks.OfType<Table>().Single().PreservedXml, StringComparison.Ordinal);
    }

    /// <summary>§17.4.61: the same, for a mark between two cells of one row.</summary>
    [Fact]
    public async Task AMarkBetweenTableCells_SurvivesTheRoundTrip()
    {
        WordDocument document = OneCellTable(out Table table);
        table.Rows[0].PreservedXml = "<w:permStart w:id=\"3\" w:edGrp=\"everyone\"/><w:permEnd w:id=\"3\"/>";

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "a permission mark between cells");

        Assert.Contains("everyone", reloaded.Blocks.OfType<Table>().Single().Rows[0].PreservedXml, StringComparison.Ordinal);
    }

    /// <summary>
    /// §17.9.14: the numbering part ends with the identifier Word remembers for its own
    /// renumbering, which used to be dropped along with the rest of what the reader skipped.
    /// </summary>
    [Fact]
    public async Task TheNumberingCleanupMarker_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("item");
        document.Numbering.AddBulletList();
        document.Numbering.CleanupXml = "<w:numIdMacAtCleanup w:val=\"5\"/>";

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "a numbering cleanup marker");

        Assert.Contains("w:numIdMacAtCleanup", reloaded.Numbering.CleanupXml, StringComparison.Ordinal);
        Assert.Contains("w:val=\"5\"", reloaded.Numbering.CleanupXml, StringComparison.Ordinal);
    }

    private static WordDocument OneCellTable(out Table table)
    {
        WordDocument document = WordDocument.Create();
        table = document.Sections[0].AddTable(1, 2);
        table.Grid.Clear();
        table.Grid.AddRange([Length.FromTwips(2400), Length.FromTwips(2400)]);
        table[0, 0].SetText("left");
        table[0, 1].SetText("right");
        return document;
    }
}
