using Quillwright.IO;

namespace Quillwright.Tests;

/// <summary>
/// Part names and the two other strings that stand for them: the URI in a relationship's
/// <c>Target</c>, and the item name inside the ZIP.
/// </summary>
/// <remarks>
/// ECMA-376 part 2 keeps these apart on purpose. A <c>Target</c> is a URI reference resolved
/// against the source part's name (§9.3), so anything a URI cannot spell is percent-encoded;
/// a ZIP item name drops the leading slash and percent-encodes every non-ASCII character
/// (§7.3.4). Treating any of the three as interchangeable works right up until a part is
/// called something with a space in it.
/// </remarks>
public class OpcPathTests
{
    [Theory]
    [InlineData("/word/document.xml", "styles.xml", "/word/styles.xml")]
    [InlineData("/word/document.xml", "media/image1.png", "/word/media/image1.png")]
    [InlineData("/word/document.xml", "/word/media/image1.png", "/word/media/image1.png")]
    [InlineData("/word/header1.xml", "../media/image1.png", "/media/image1.png")]
    public void AnOrdinaryTarget_ResolvesToThePartItNames(string source, string target, string expected) =>
        Assert.Equal(expected, OpcPath.Resolve(source, target));

    /// <summary>
    /// §9.3: the target is a URI, so the part name is what it unescapes to. Looking the part
    /// up under the escaped spelling finds nothing at all.
    /// </summary>
    [Theory]
    [InlineData("media/image%201.png", "/word/media/image 1.png")]
    [InlineData("media/%D1%81%D1%85%D0%B5%D0%BC%D0%B0.png", "/word/media/схема.png")]
    [InlineData("media/100%25.png", "/word/media/100%.png")]
    public void AnEscapedTarget_ResolvesToTheNameItStandsFor(string target, string expected) =>
        Assert.Equal(expected, OpcPath.Resolve("/word/document.xml", target));

    /// <summary>Writing a target is the same journey in reverse.</summary>
    [Theory]
    [InlineData("/word/media/image1.png", "media/image1.png")]
    [InlineData("/word/media/image 1.png", "media/image%201.png")]
    [InlineData("/word/media/100%.png", "media/100%25.png")]
    public void ATargetIsWritten_AsAUriRatherThanAName(string part, string expected) =>
        Assert.Equal(expected, OpcPath.MakeRelative("/word/document.xml", part));

    [Theory]
    [InlineData("/word/media/image1.png")]
    [InlineData("/word/media/image 1.png")]
    [InlineData("/word/media/схема.png")]
    [InlineData("/word/media/100%.png")]
    public void APartName_SurvivesBeingWrittenAsATargetAndReadBack(string part) =>
        Assert.Equal(part, OpcPath.Resolve("/word/document.xml", OpcPath.MakeRelative("/word/document.xml", part)));

    /// <summary>
    /// §7.3.4: a ZIP item name percent-encodes every non-ASCII character and nothing else, so
    /// a name that is already ASCII — which is every part Word writes — is untouched.
    /// </summary>
    [Theory]
    [InlineData("/word/document.xml", "word/document.xml")]
    [InlineData("/word/media/image 1.png", "word/media/image 1.png")]
    [InlineData("/word/media/схема.png", "word/media/%D1%81%D1%85%D0%B5%D0%BC%D0%B0.png")]
    public void AZipItemName_EncodesOnlyWhatTheStandardAsksFor(string part, string expected) =>
        Assert.Equal(expected, OpcPath.ToEscapedEntryName(part));
}
