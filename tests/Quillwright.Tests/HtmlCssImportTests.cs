using Quillwright.Html;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Tests;

/// <summary>CSS2 lexical edges whose decoded values feed the HTML importer's supported subset.</summary>
public class HtmlCssImportTests
{
    [Fact]
    public void EscapedPropertyAndKeywordIdentifiers_AreDecodedBeforeInterpretation()
    {
        WordDocument document = HtmlImporter.Import(
            "<p><span style=\"f\\6f nt-family:'Escaped Face'\">f</span>" +
            "<span style=\"color:\\72 ed\">c</span></p>").Document;
        Paragraph paragraph = document.Paragraphs.Single();

        Assert.Equal("Escaped Face", FormatAt(paragraph, "f").FontAscii);
        Assert.Equal(0xFF0000u, FormatAt(paragraph, "c").Color?.Rgb);
    }

    [Fact]
    public void EscapedInheritKeyword_InheritsInsteadOfNamingAFont()
    {
        WordDocument document = HtmlImporter.Import(
            "<p><span style=\"font-family:Parent Face\">" +
            "<span style=\"font-family:In\\68 erit\">x</span></span></p>").Document;

        Assert.Equal("Parent Face", FormatAt(document.Paragraphs.Single(), "x").FontAscii);
    }

    [Fact]
    public void EscapedImportantKeyword_ParticipatesInCascadeOrder()
    {
        WordDocument document = HtmlImporter.Import(
            "<p><span style=\"color:blue!important;color:red !\\69mportant\">x</span></p>").Document;

        Assert.Equal(0xFF0000u, FormatAt(document.Paragraphs.Single(), "x").Color?.Rgb);
    }

    [Theory]
    [InlineData("font-family:Red/Black")]
    [InlineData("font-family:Arial,")]
    [InlineData("font-family:Arial, 'Lucida' Grande")]
    public void InvalidFontFamilyGrammar_DoesNotOverrideTheInheritedFamily(string declaration)
    {
        WordDocument document = HtmlImporter.Import(
            $"<p><span style=\"font-family:Parent Face\"><span style=\"{declaration}\">x</span></span></p>").Document;

        Assert.Equal("Parent Face", FormatAt(document.Paragraphs.Single(), "x").FontAscii);
    }

    [Fact]
    public void NonBreakingSpace_IsPartOfAnIdentifierRatherThanCssWhitespace()
    {
        WordDocument document = HtmlImporter.Import(
            "<p><span style=\"font-family: Face \">x</span></p>").Document;

        Assert.Equal(" Face ", FormatAt(document.Paragraphs.Single(), "x").FontAscii);
    }

    private static RunFormat FormatAt(Paragraph paragraph, string text)
    {
        int offset = paragraph.Text.IndexOf(text, StringComparison.Ordinal);
        Assert.True(offset >= 0);
        return paragraph.FormatAtOffset(offset);
    }
}
