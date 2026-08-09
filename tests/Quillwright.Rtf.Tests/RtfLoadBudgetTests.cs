using System.Text;
using Quillwright.Diagnostics;

namespace Quillwright.Rtf.Tests;

public sealed class RtfLoadBudgetTests
{
    [Fact]
    public void CommonBudgetHasInclusiveRtfBoundaries()
    {
        byte[] content = Encoding.ASCII.GetBytes(@"{\rtf1 xy}");
        var exact = new RtfImportOptions
        {
            Budget = DocumentLoadBudget.Default with
            {
                MaxInputBytes = content.Length,
                MaxMarkupDepth = 1,
                MaxTextCharacters = 2,
            },
        };

        Assert.Equal("xy", RtfReader.Load(content, exact).Document.GetText());
        Assert.Throws<RtfFormatException>(() => RtfReader.Load(content, exact with
        {
            Budget = exact.Budget with { MaxInputBytes = content.Length - 1 },
        }));
        Assert.Throws<RtfFormatException>(() => RtfReader.Load(content, exact with
        {
            Budget = exact.Budget with { MaxTextCharacters = 1 },
        }));
    }

    [Fact]
    public async Task StreamReaderUsesCommonInputGuard_AndKeepsRtfExceptionContract()
    {
        byte[] content = Encoding.ASCII.GetBytes(@"{\rtf1 bounded}");
        var options = new RtfImportOptions
        {
            Budget = DocumentLoadBudget.Default with { MaxInputBytes = content.Length - 1 },
        };

        RtfFormatException error = await Assert.ThrowsAsync<RtfFormatException>(async () =>
            await RtfReader.LoadAsync(
                new MemoryStream(content), options, TestContext.Current.CancellationToken));

        Assert.Contains($"{content.Length - 1}-byte limit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyAliasesUpdateTheSharedBudget()
    {
        var options = new RtfImportOptions
        {
            MaxInputBytes = 17,
            MaxGroupDepth = 3,
            MaxTextCharacters = 11,
        };

        Assert.Equal(17, options.Budget.MaxInputBytes);
        Assert.Equal(3, options.Budget.MaxMarkupDepth);
        Assert.Equal(11, options.Budget.MaxTextCharacters);
    }
}
