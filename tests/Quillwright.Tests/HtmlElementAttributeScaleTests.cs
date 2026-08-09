using System.Text;
using Quillwright.Html;

namespace Quillwright.Tests;

/// <summary>Deterministic complexity guards for attributes after tokenization.</summary>
public class HtmlElementAttributeScaleTests
{
    [Fact]
    public void AddAttributes_NewWideElementUsesOneIndexedInsertionPerAttribute()
    {
        const int AttributeCount = 2_048;
        var comparer = new CountingAttributeNameComparer();
        var element = new HtmlElement("div", HtmlNamespace.Html, comparer);
        HtmlAttribute[] attributes = [.. Enumerable.Range(0, AttributeCount)
            .Select(static index => Attribute(index, "value-" + index))];

        element.AddAttributes(attributes);

        Assert.Equal(AttributeCount, element.Attributes.Count);
        Assert.Equal(AttributeCount, comparer.HashCalls);
        Assert.Equal(0, comparer.EqualityCalls);
    }

    [Fact]
    public void Parser_CreatesOneWideOrdinaryElementThroughTheIndexedAttributePath()
    {
        const int AttributeCount = 2_048;
        var source = new StringBuilder("<div");
        for (int index = 0; index < AttributeCount; index++)
            source.Append(" a").Append(index).Append("='").Append(index).Append("'");
        source.Append('>');

        HtmlElement document = HtmlParser.Parse(source.ToString());
        HtmlElement html = Assert.Single(document.Children.OfType<HtmlElement>());
        HtmlElement body = Assert.Single(html.Children.OfType<HtmlElement>(), static child => child.Is("body"));
        HtmlElement div = Assert.Single(body.Children.OfType<HtmlElement>(), static child => child.Is("div"));

        Assert.Equal(AttributeCount, div.Attributes.Count);
        Assert.Equal(new HtmlAttribute("a0", "0"), div.Attributes[0]);
        Assert.Equal(new HtmlAttribute("a1024", "1024"), div.Attributes[1024]);
        Assert.Equal(new HtmlAttribute("a2047", "2047"), div.Attributes[^1]);
    }

    [Fact]
    public void SameAs_ManyAttributesUsesOneIndexedLookupPerAttributeRegardlessOfOrder()
    {
        const int AttributeCount = 2_048;
        var left = new HtmlElement("b");
        var comparer = new CountingAttributeNameComparer();
        var right = new HtmlElement("b", HtmlNamespace.Html, comparer);
        for (int index = 0; index < AttributeCount; index++)
            left.AddAttribute(Attribute(index, "value-" + index));
        for (int index = AttributeCount - 1; index >= 0; index--)
            right.AddAttribute(Attribute(index, "value-" + index));

        comparer.Reset();

        Assert.True(left.SameAs(right));
        Assert.Equal(AttributeCount, comparer.HashCalls);
        Assert.Equal(AttributeCount, comparer.EqualityCalls);
    }

    [Fact]
    public void AddAttributes_MergesWithIndexedFirstWinsLookupsAndPreservesOrder()
    {
        const int AttributeCount = 2_048;
        var comparer = new CountingAttributeNameComparer();
        var element = new HtmlElement("html", HtmlNamespace.Html, comparer);
        for (int index = 0; index < AttributeCount; index++)
            element.AddAttribute(Attribute(index, "original-" + index));

        comparer.Reset();
        for (int index = AttributeCount - 1; index >= 0; index--)
            element.AddAttribute(Attribute(index, "replacement-" + index));
        element.AddAttribute(Attribute(AttributeCount, "appended"));

        Assert.Equal(AttributeCount + 1, element.Attributes.Count);
        Assert.Equal(new HtmlAttribute("a0", "original-0"), element.Attributes[0]);
        Assert.Equal(
            new HtmlAttribute("a" + (AttributeCount - 1), "original-" + (AttributeCount - 1)),
            element.Attributes[AttributeCount - 1]);
        Assert.Equal(
            new HtmlAttribute("a" + AttributeCount, "appended"),
            element.Attributes[AttributeCount]);
        Assert.Equal(AttributeCount + 1, comparer.HashCalls);
        Assert.Equal(AttributeCount, comparer.EqualityCalls);
    }

    [Fact]
    public void RepeatedHtmlAndBodyTags_MergeNewAttributesWithoutReplacingEarlierValues()
    {
        HtmlElement document = HtmlParser.Parse(
            "<html a='first'><body x='first'><html a='second' b='added'>" +
            "<body x='second' y='added'>");
        HtmlElement html = Assert.Single(document.Children.OfType<HtmlElement>());
        HtmlElement body = Assert.Single(html.Children.OfType<HtmlElement>(), static child => child.Is("body"));

        Assert.Collection(
            html.Attributes,
            attribute => Assert.Equal(new HtmlAttribute("a", "first"), attribute),
            attribute => Assert.Equal(new HtmlAttribute("b", "added"), attribute));
        Assert.Collection(
            body.Attributes,
            attribute => Assert.Equal(new HtmlAttribute("x", "first"), attribute),
            attribute => Assert.Equal(new HtmlAttribute("y", "added"), attribute));
    }

    [Fact]
    public void SameAs_StillRequiresTheSameNameNamespaceAndValues()
    {
        var html = new HtmlElement("b");
        html.AddAttribute(new HtmlAttribute("class", "one"));
        var differentValue = new HtmlElement("b");
        differentValue.AddAttribute(new HtmlAttribute("class", "two"));
        var differentName = new HtmlElement("i");
        differentName.AddAttribute(new HtmlAttribute("class", "one"));
        var differentNamespace = new HtmlElement("b", HtmlNamespace.Svg);
        differentNamespace.AddAttribute(new HtmlAttribute("class", "one"));

        Assert.False(html.SameAs(differentValue));
        Assert.False(html.SameAs(differentName));
        Assert.False(html.SameAs(differentNamespace));
    }

    private static HtmlAttribute Attribute(int index, string value) =>
        new("a" + index, value);

    private sealed class CountingAttributeNameComparer : IEqualityComparer<string>
    {
        public int HashCalls { get; private set; }

        public int EqualityCalls { get; private set; }

        public bool Equals(string? x, string? y)
        {
            EqualityCalls++;
            return string.Equals(x, y, StringComparison.Ordinal);
        }

        public int GetHashCode(string value)
        {
            HashCalls++;
            int hash = 0;
            for (int index = 1; index < value.Length; index++)
                hash = (hash * 10) + (value[index] - '0');
            return hash;
        }

        public void Reset()
        {
            HashCalls = 0;
            EqualityCalls = 0;
        }
    }
}
