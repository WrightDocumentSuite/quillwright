using System.Text;
using Quillwright.Html;

namespace Quillwright.Tests;

/// <summary>Complexity guards for attacker-controlled shapes inside a single HTML token.</summary>
public class HtmlTokenizerScaleTests
{
    [Fact]
    public void ManyUniqueAttributes_UseOneHashLookupPerAttribute()
    {
        const int AttributeCount = 4_096;
        var html = new StringBuilder("<x");
        for (int i = 0; i < AttributeCount; i++)
            html.Append(" a").Append(i).Append('=').Append(i);

        html.Append(" a2048=duplicate>");
        var comparer = new CountingAttributeNameComparer();
        var tokenizer = new HtmlTokenizer(html.ToString(), static () => false, comparer);

        HtmlToken token = tokenizer.Next();

        Assert.Equal(HtmlTokenKind.StartTag, token.Kind);
        Assert.Equal(AttributeCount, token.Attributes.Count);
        Assert.Equal("2048", token.Attributes.Single(attribute => attribute.Name == "a2048").Value);
        Assert.Equal(AttributeCount + 1, comparer.HashCalls);
        Assert.Equal(1, comparer.EqualityCalls);
    }

    [Fact]
    public void DuplicateAttributeSet_UsesNormalizedExactNamesAndKeepsTheFirstValue()
    {
        var tokenizer = new HtmlTokenizer(
            "<x A=first a=second Ä=upper ä=lower>",
            static () => false);

        HtmlToken token = tokenizer.Next();

        Assert.Collection(
            token.Attributes,
            attribute => Assert.Equal(new HtmlAttribute("a", "first"), attribute),
            attribute => Assert.Equal(new HtmlAttribute("Ä", "upper"), attribute),
            attribute => Assert.Equal(new HtmlAttribute("ä", "lower"), attribute));
    }

    [Fact]
    public void LongDoctypeIdentifiers_AllocateLinearly()
    {
        const int IdentifierLength = 4_096;
        string identifier = new('x', IdentifierLength);
        string html = $"<!DOCTYPE html PUBLIC \"{identifier}\" \"{identifier}\">";

        // Keep one-time JIT and type-initialization allocations outside the measurement.
        _ = ReadDoctype("<!DOCTYPE html PUBLIC \"warm\" \"up\">");

        long before = GC.GetAllocatedBytesForCurrentThread();
        HtmlToken token = ReadDoctype(html);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(identifier, token.PublicIdentifier);
        Assert.Equal(identifier, token.SystemIdentifier);
        Assert.True(
            allocated < html.Length * 128L,
            $"Tokenizing {html.Length} characters allocated {allocated:N0} bytes.");
    }

    [Theory]
    [InlineData("<!DOCTYPE html>", null, null, false)]
    [InlineData("<!DOCTYPE html PUBLIC>", null, null, true)]
    [InlineData("<!DOCTYPE html SYSTEM>", null, null, true)]
    [InlineData("<!DOCTYPE html PUBLIC", null, null, true)]
    [InlineData("<!DOCTYPE html SYSTEM", null, null, true)]
    [InlineData("<!DOCTYPE html PUBLICx>", null, null, true)]
    [InlineData("<!DOCTYPE html SYSTEMx>", null, null, true)]
    [InlineData("<!DOCTYPE html PUBLIC\"\">", "", null, false)]
    [InlineData("<!DOCTYPE html PUBLIC''>", "", null, false)]
    [InlineData("<!DOCTYPE html SYSTEM\"\">", null, "", false)]
    [InlineData("<!DOCTYPE html SYSTEM''>", null, "", false)]
    [InlineData("<!DOCTYPE html PUBLIC \"\" \"\">", "", "", false)]
    [InlineData("<!DOCTYPE html SYSTEM \"\">", null, "", false)]
    public void DoctypeIdentifiers_PreserveMissingAndEmptyValues(
        string html,
        string? expectedPublicIdentifier,
        string? expectedSystemIdentifier,
        bool expectedForceQuirks)
    {
        HtmlToken token = ReadDoctype(html);

        Assert.Equal(expectedPublicIdentifier, token.PublicIdentifier);
        Assert.Equal(expectedSystemIdentifier, token.SystemIdentifier);
        Assert.Equal(expectedForceQuirks, token.ForceQuirks);
    }

    private static HtmlToken ReadDoctype(string html)
    {
        var tokenizer = new HtmlTokenizer(html, static () => false);
        HtmlToken token = tokenizer.Next();
        Assert.Equal(HtmlTokenKind.Doctype, token.Kind);
        return token;
    }

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
            for (int i = 1; i < value.Length; i++)
                hash = (hash * 10) + (value[i] - '0');

            return hash;
        }
    }
}
