using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Tests;

/// <summary>
/// How a document says its notes are printed and numbered (<c>w:footnotePr</c>,
/// <c>w:endnotePr</c>). The element is kept verbatim; this is a reading of it, so there is
/// nothing to set and nothing that can drift out of step with what is written back.
/// </summary>
public class NotePropertiesTests
{
    [Fact]
    public void ADocumentThatSaysNothing_GetsWhatWordWouldDo()
    {
        WordDocument document = WordDocument.Create();

        Assert.Equal(NotePosition.PageBottom, document.Settings.Footnotes.Position);
        Assert.Equal(ListNumberFormat.Decimal, document.Settings.Footnotes.NumberFormat);
        Assert.Equal(NoteRestart.Continuous, document.Settings.Footnotes.Restart);
        Assert.Equal(1, document.Settings.Footnotes.Start);
    }

    /// <summary>Endnotes count differently from footnotes, and go somewhere else.</summary>
    [Fact]
    public void EndnotesDefaultToRomanAtTheEnd()
    {
        WordDocument document = WordDocument.Create();

        Assert.Equal(NotePosition.DocumentEnd, document.Settings.Endnotes.Position);
        Assert.Equal(ListNumberFormat.LowerRoman, document.Settings.Endnotes.NumberFormat);
    }

    [Fact]
    public void EverythingTheElementStatesIsRead()
    {
        WordDocument document = WordDocument.Create();
        document.Settings.SetRaw("footnotePr", """
            <w:footnotePr>
              <w:pos w:val="beneathText"/>
              <w:numFmt w:val="upperLetter"/>
              <w:numStart w:val="4"/>
              <w:numRestart w:val="eachSect"/>
            </w:footnotePr>
            """);

        NoteProperties notes = document.Settings.Footnotes;

        Assert.Equal(NotePosition.BeneathText, notes.Position);
        Assert.Equal(ListNumberFormat.UpperLetter, notes.NumberFormat);
        Assert.Equal(4, notes.Start);
        Assert.Equal(NoteRestart.EachSection, notes.Restart);
    }

    /// <summary>What the element does not state keeps the value the format gives it.</summary>
    [Fact]
    public void WhatIsLeftOutKeepsItsDefault()
    {
        WordDocument document = WordDocument.Create();
        document.Settings.SetRaw("endnotePr", "<w:endnotePr><w:numFmt w:val=\"decimal\"/></w:endnotePr>");

        NoteProperties notes = document.Settings.Endnotes;

        Assert.Equal(ListNumberFormat.Decimal, notes.NumberFormat);
        Assert.Equal(NotePosition.DocumentEnd, notes.Position);
    }

    [Fact]
    public void ASectionSpeaksForItselfOrNotAtAll()
    {
        WordDocument document = WordDocument.Create();
        SectionProperties properties = document.Sections[0].Properties;

        Assert.Null(properties.FootnoteProperties);

        properties.FootnotePropertiesXml = "<w:footnotePr><w:numFmt w:val=\"lowerRoman\"/></w:footnotePr>";

        Assert.Equal(ListNumberFormat.LowerRoman, properties.FootnoteProperties?.NumberFormat);
    }

    /// <summary>Markup that cannot be read is still written back, so it must not throw here.</summary>
    [Fact]
    public void MarkupThatMakesNoSenseFallsBackToTheDefaults()
    {
        WordDocument document = WordDocument.Create();
        document.Settings.SetRaw("footnotePr", "<w:footnotePr><w:pos w:val=");

        Assert.Equal(NotePosition.PageBottom, document.Settings.Footnotes.Position);
    }

    /// <summary>The element survives a round trip whatever this made of it.</summary>
    [Fact]
    public async Task TheElementItselfIsWrittenBack()
    {
        WordDocument document = WordDocument.Create();
        // The schema fixes the order of these, and the document is checked against it.
        document.Sections[0].Properties.FootnotePropertiesXml =
            "<w:footnotePr><w:pos w:val=\"beneathText\"/><w:numFmt w:val=\"chicago\"/></w:footnotePr>";

        WordDocument reopened = await DocumentFixture.RoundTripAsync(document, "a document with note settings");
        SectionProperties properties = reopened.Sections[0].Properties;

        Assert.Contains("chicago", properties.FootnotePropertiesXml ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(NotePosition.BeneathText, properties.FootnoteProperties?.Position);

        // A scheme this version does not know is kept as itself rather than being turned into one it does.
        Assert.Equal(ListNumberFormat.Custom, properties.FootnoteProperties?.NumberFormat);
    }
}
