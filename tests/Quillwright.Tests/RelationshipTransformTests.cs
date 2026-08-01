using System.Text;
using System.Xml.Linq;
using Quillwright.IO;

namespace Quillwright.Tests;

/// <summary>The OPC relationships transform, tested from the rules in ECMA-376-2 clause 10.6.</summary>
public class RelationshipTransformTests
{
    private const string Relationships =
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"urn:example:Type\" Target=\"one.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"urn:example:Other\" Target=\"two.xml\"/>" +
        "</Relationships>";

    [Theory]
    [InlineData("RID1", null)]
    [InlineData(null, "URN:EXAMPLE:TYPE")]
    public void SourceIdAndSourceType_AreComparedAsciiCaseInsensitively(string? id, string? type)
    {
        XDocument? transformed = RelationshipTransform.Apply(
            Encoding.UTF8.GetBytes(Relationships),
            new RelationshipSelection(id is null ? [] : [id], type is null ? [] : [type]));

        Assert.NotNull(transformed);

        XElement relationship = Assert.Single(transformed.Root!.Elements());
        Assert.Equal("rId1", relationship.Attribute("Id")?.Value);
    }

    [Fact]
    public void ATransformWithoutAnyRelationshipReference_IsInvalid()
    {
        Assert.Null(RelationshipTransform.Apply(
            Encoding.UTF8.GetBytes(Relationships),
            new RelationshipSelection([], [])));
    }

    [Fact]
    public void MatchingFoldsAsciiLettersButNotNonAsciiLetters()
    {
        const string Markup =
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"résumé\" Type=\"urn:example\" Target=\"one.xml\"/>" +
            "</Relationships>";

        XDocument? transformed = RelationshipTransform.Apply(
            Encoding.UTF8.GetBytes(Markup),
            new RelationshipSelection(["RÉSUMÉ"], []));

        Assert.NotNull(transformed);
        Assert.Empty(transformed.Root!.Elements());
    }

    [Fact]
    public void MceProcessContent_UnwrapsAnIgnorableExtensionBeforeSelection()
    {
        const string Markup =
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\" " +
            "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" " +
            "xmlns:x=\"urn:future\" mc:Ignorable=\"x\" mc:ProcessContent=\"x:wrapper\">" +
            "<x:wrapper><Relationship Id=\"rId1\" Type=\"urn:example\" Target=\"one.xml\"/></x:wrapper>" +
            "</Relationships>";

        XDocument? transformed = RelationshipTransform.Apply(
            Encoding.UTF8.GetBytes(Markup),
            new RelationshipSelection(["rId1"], []));

        Assert.NotNull(transformed);
        Assert.Equal("rId1", Assert.Single(transformed.Root!.Elements()).Attribute("Id")?.Value);
    }

    [Fact]
    public void MceAlternateContent_SelectsTheFallbackForAnUnknownRequiredNamespace()
    {
        const string Markup =
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\" " +
            "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:x=\"urn:future\">" +
            "<mc:AlternateContent><mc:Choice Requires=\"x\">" +
            "<Relationship Id=\"future\" Type=\"urn:example\" Target=\"future.xml\"/>" +
            "</mc:Choice><mc:Fallback>" +
            "<Relationship Id=\"rId1\" Type=\"urn:example\" Target=\"one.xml\"/>" +
            "</mc:Fallback></mc:AlternateContent></Relationships>";

        XDocument? transformed = RelationshipTransform.Apply(
            Encoding.UTF8.GetBytes(Markup),
            new RelationshipSelection(["rId1"], []));

        Assert.NotNull(transformed);
        Assert.Equal("rId1", Assert.Single(transformed.Root!.Elements()).Attribute("Id")?.Value);
    }
}
