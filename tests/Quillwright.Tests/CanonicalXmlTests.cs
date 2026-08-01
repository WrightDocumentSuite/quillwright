using System.Text;
using System.Xml.Linq;
using Quillwright.IO;

namespace Quillwright.Tests;

/// <summary>
/// Canonical XML, one rule of the specification at a time.
/// </summary>
/// <remarks>
/// <para>
/// Verifying a signature means producing byte for byte what the signer hashed, so a
/// canonicaliser is either exactly right or useless. Every expectation here is written out by
/// hand from
/// <see href="https://www.w3.org/TR/2001/REC-xml-c14n-20010315">Canonical XML 1.0</see> and
/// <see href="https://www.w3.org/TR/xml-exc-c14n/">Exclusive XML Canonicalization</see> rather
/// than recorded from this implementation, so a test passes because the code agrees with the
/// specification and not because it agrees with itself.
/// </para>
/// <para>
/// The library carries its own canonicaliser because
/// <c>System.Security.Cryptography.Xml</c> is not guaranteed to survive trimming and this
/// package is meant to. That is also why it is not a test dependency: an oracle that cannot
/// ship is an oracle that has to be kept honest some other way, and the specification is the
/// better authority anyway.
/// </para>
/// </remarks>
public class CanonicalXmlTests
{
    /// <summary>Every rule, as the markup that exercises it and what canonicalising it gives.</summary>
    public static TheoryData<string, string, string> Cases => new()
    {
        // An empty element is written as a pair of tags, whichever way it arrived.
        { "an empty element", "<a/>", "<a></a>" },
        { "an element written as a pair", "<a></a>", "<a></a>" },
        { "text content", "<a>text</a>", "<a>text</a>" },

        // Attributes are sorted by namespace and then by local name.
        { "attributes out of order", "<a b=\"2\" a=\"1\"/>", "<a a=\"1\" b=\"2\"></a>" },
        {
            "a qualified attribute after an unqualified one",
            "<a xmlns:o=\"urn:o\"><b o:x=\"1\" a=\"2\"/></a>",
            "<a xmlns:o=\"urn:o\"><b a=\"2\" o:x=\"1\"></b></a>"
        },

        // A declaration is written where it first appears and not repeated below it.
        { "an inherited default namespace", "<a xmlns=\"urn:d\"><b/></a>", "<a xmlns=\"urn:d\"><b></b></a>" },
        {
            "declarations sorted by prefix",
            "<a xmlns:p=\"urn:p\" xmlns:o=\"urn:o\"><p:b/></a>",
            "<a xmlns:o=\"urn:o\" xmlns:p=\"urn:p\"><p:b></p:b></a>"
        },

        // The default namespace being taken away has to be written, or the child moves into it.
        {
            "a default namespace undeclared by a child",
            "<a xmlns=\"urn:d\"><b xmlns=\"\"/></a>",
            "<a xmlns=\"urn:d\"><b xmlns=\"\"></b></a>"
        },

        // The two escaping tables differ: a tab is escaped in an attribute and not in text,
        // and a closing bracket is escaped in text and not in an attribute.
        {
            "the characters that are escaped differently",
            "<a b=\"&lt;&amp;&quot;&#x9;&gt;\">&lt;&amp;&gt;\t</a>",
            "<a b=\"&lt;&amp;&quot;&#x9;>\">&lt;&amp;&gt;\t</a>"
        },

        // Whitespace between elements is content, and canonicalisation keeps every character.
        { "whitespace between elements", "<a>\n  <b/>\n</a>", "<a>\n  <b></b>\n</a>" },
        { "a processing instruction", "<a><?work now?><b/></a>", "<a><?work now?><b></b></a>" },

        // CDATA is a way of writing characters, not a thing of its own.
        { "a CDATA section", "<a><![CDATA[<raw>]]></a>", "<a>&lt;raw&gt;</a>" },

        // The xml prefix is bound without being declared, so it is never written.
        {
            "an attribute in the xml namespace",
            "<a xml:lang=\"en\"><b xml:lang=\"fr\"/></a>",
            "<a xml:lang=\"en\"><b xml:lang=\"fr\"></b></a>"
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void InclusiveCanonicalisation_FollowsTheSpecification(string rule, string markup, string expected)
    {
        Assert.Equal(expected, Canonical(markup, CanonicalXml.Inclusive));
        Assert.NotEmpty(rule);
    }

    /// <summary>
    /// The one rule the two forms differ over: inclusive carries every declaration in scope,
    /// exclusive only the ones the element and its attribute names actually use.
    /// </summary>
    [Fact]
    public void ExclusiveCanonicalisation_LeavesOutADeclarationNothingUses()
    {
        const string Markup = "<a xmlns:unused=\"urn:unused\"><b>t</b></a>";

        Assert.Equal("<a xmlns:unused=\"urn:unused\"><b>t</b></a>", Canonical(Markup, CanonicalXml.Inclusive));
        Assert.Equal("<a><b>t</b></a>", Canonical(Markup, CanonicalXml.Exclusive));
    }

    [Fact]
    public void ExclusiveCanonicalisation_MovesADeclarationDownToWhereItIsUsed()
    {
        const string Markup = "<a xmlns:p=\"urn:p\" xmlns:o=\"urn:o\"><p:b o:x=\"1\"/></a>";

        Assert.Equal(
            "<a xmlns:o=\"urn:o\" xmlns:p=\"urn:p\"><p:b o:x=\"1\"></p:b></a>",
            Canonical(Markup, CanonicalXml.Inclusive));

        Assert.Equal(
            "<a><p:b xmlns:o=\"urn:o\" xmlns:p=\"urn:p\" o:x=\"1\"></p:b></a>",
            Canonical(Markup, CanonicalXml.Exclusive));
    }

    /// <summary>
    /// The case that matters most, because it is the one a signature uses: an element in the
    /// middle of a document, canonicalised with the namespaces its ancestors put in scope
    /// rather than the ones it declares itself.
    /// </summary>
    [Fact]
    public void AnElementInsideADocument_CarriesTheNamespacesItsAncestorsDeclared()
    {
        const string Markup =
            "<root xmlns=\"urn:outer\" xmlns:p=\"urn:p\"><wrap><target><p:leaf/></target></wrap></root>";

        Assert.Equal(
            "<target xmlns=\"urn:outer\" xmlns:p=\"urn:p\"><p:leaf></p:leaf></target>",
            Canonical(Markup, CanonicalXml.Inclusive, "target"));
    }

    [Fact]
    public void AnElementInsideADocument_CarriesTheXmlAttributesItsAncestorsSet()
    {
        const string Markup = "<root xml:lang=\"en\" xml:space=\"preserve\"><target xml:lang=\"fr\"/></root>";

        // The nearer statement of the same attribute wins; the other is inherited.
        Assert.Equal(
            "<target xml:lang=\"fr\" xml:space=\"preserve\"></target>",
            Canonical(Markup, CanonicalXml.Inclusive, "target"));

        // Exclusive canonicalisation exists so a fragment can be moved, and inheriting the
        // context it was moved out of would defeat that.
        Assert.Equal("<target xml:lang=\"fr\"></target>", Canonical(Markup, CanonicalXml.Exclusive, "target"));
    }

    [Fact]
    public void ExclusiveCanonicalisationOfAnElementInside_LeavesOutWhatItDoesNotUse()
    {
        const string Markup = "<root xmlns:p=\"urn:p\" xmlns:q=\"urn:q\"><target><p:leaf/></target></root>";

        Assert.Equal("<target><p:leaf xmlns:p=\"urn:p\"></p:leaf></target>", Canonical(Markup, CanonicalXml.Exclusive, "target"));
    }

    [Fact]
    public void AnAlgorithmThatIsNotACanonicalisation_IsRefused()
    {
        Assert.False(CanonicalXml.Supports("http://www.w3.org/2000/09/xmldsig#base64"));
        Assert.False(CanonicalXml.Supports(null));
        Assert.True(CanonicalXml.Supports(CanonicalXml.Inclusive));
        Assert.True(CanonicalXml.Supports(CanonicalXml.ExclusiveWithComments));
    }

    [Theory]
    [InlineData(CanonicalXml.Inclusive, "<a><b></b></a>")]
    [InlineData(CanonicalXml.Exclusive, "<a><b></b></a>")]
    [InlineData(CanonicalXml.InclusiveWithComments, "<a><!--covered--><b></b></a>")]
    [InlineData(CanonicalXml.ExclusiveWithComments, "<a><!--covered--><b></b></a>")]
    public void WithCommentsAlgorithms_KeepCommentsAndOnlyThoseAlgorithmsDo(
        string algorithm, string expected)
    {
        Assert.Equal(expected, Canonical("<a><!--covered--><b/></a>", algorithm));
    }

    [Fact]
    public void AnUnqualifiedAttribute_DoesNotHideAnInheritedXmlAttributeWithTheSameLocalName()
    {
        const string Markup = "<root xml:lang=\"en\"><target lang=\"literal\"/></root>";

        Assert.Equal(
            "<target lang=\"literal\" xml:lang=\"en\"></target>",
            Canonical(Markup, CanonicalXml.Inclusive, "target"));
    }

    private static string Canonical(string markup, string algorithm, string? element = null)
    {
        XDocument document = CanonicalXml.Parse(markup);
        XElement target = element is null
            ? document.Root!
            : document.Descendants().Single(node => node.Name.LocalName == element);

        return Encoding.UTF8.GetString(CanonicalXml.Canonicalize(target, algorithm));
    }
}
