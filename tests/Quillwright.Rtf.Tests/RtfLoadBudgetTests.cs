using System.Text;
using Quillwright.Diagnostics;
using Quillwright.Rtf.Parsing;

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

    [Theory]
    [InlineData(@"\par ")]
    [InlineData(@"{\*\annotation}")]
    public void MillionMaterializedNodes_StopAtTheSharedMarkupBudget(string repeatedToken)
    {
        byte[] content = RepeatedRtf(repeatedToken, 1_000_000);
        var options = new RtfImportOptions
        {
            Budget = DocumentLoadBudget.Default with { MaxMarkupNodes = 32 },
        };

        RtfFormatException error = Assert.Throws<RtfFormatException>(() => RtfReader.Load(content, options));

        Assert.Contains(nameof(DocumentLoadBudget.MaxMarkupNodes), error.Message, StringComparison.Ordinal);
        Assert.Contains("32-node", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnnotationParagraphRecordAndCommentShareOneInclusiveMarkupBudget()
    {
        byte[] content = Encoding.ASCII.GetBytes(
            @"{\rtf1 Text{\*\atnid A}\chatn {\*\annotation\pard Note\par}}");
        var exact = new RtfImportOptions
        {
            Budget = DocumentLoadBudget.Default with { MaxMarkupNodes = 4 },
        };

        Assert.Single(RtfReader.Load(content, exact).Document.Comments);
        RtfFormatException error = Assert.Throws<RtfFormatException>(() => RtfReader.Load(content, exact with
        {
            Budget = exact.Budget with { MaxMarkupNodes = 3 },
        }));
        Assert.Contains(nameof(DocumentLoadBudget.MaxMarkupNodes), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParserCancellationRemainsActiveAfterInputWasRead()
    {
        byte[] content = RepeatedRtf("x", 4_000_000);
        var options = new RtfImportOptions
        {
            Budget = DocumentLoadBudget.Default with { MaxTextCharacters = 4_000_000 },
        };
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueParsing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task parsing = Task.Run(async () =>
        {
            started.SetResult();
            await continueParsing.Task;
            _ = new RtfParser(options, cancellation.Token).Parse(content);
        }, TestContext.Current.CancellationToken);

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        continueParsing.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await parsing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void MultiMegabyteControlWordFailsAtTheThirtyThirdLetter()
    {
        byte[] content = Encoding.ASCII.GetBytes(@"{\rtf1\" + new string('a', 4_000_000) + "}");

        RtfFormatException error = Assert.Throws<RtfFormatException>(() => RtfReader.Load(content));

        Assert.Equal(7, error.ByteOffset);
        Assert.Contains("longer than 32 letters", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AsyncLoadHonorsAPreCancelledToken()
    {
        byte[] content = Encoding.ASCII.GetBytes(@"{\rtf1 cancellation}");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await RtfReader.LoadAsync(new MemoryStream(content), cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task AsyncLoadKeepsCancellationActiveBetweenStreamReads()
    {
        byte[] content = Encoding.ASCII.GetBytes(@"{\rtf1 streamed}");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using var stream = new CancelBeforeEofStream(content, cancellation.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await RtfReader.LoadAsync(stream, cancellationToken: cancellation.Token));
        Assert.Equal(2, stream.ReadCount);
    }

    [Fact]
    public void PictureMediaBudgets_EnforcePartByteAndPartCountLimits()
    {
        byte[] pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFUlEQVR4nGP8z8DwnwEJMKEL0FMQAG" +
            "0lAgcqA5wCAAAAAElFTkSuQmCC");
        var hex = new StringBuilder(pngBytes.Length * 2);
        foreach (byte b in pngBytes)
            hex.Append(b.ToString("x2"));

        byte[] rtfWithOnePng = Encoding.ASCII.GetBytes(
            @"{\rtf1{\pict\pngblip\picwgoal100\pichgoal100 " + hex + "}}");

        var exactPartLimit = new RtfImportOptions
        {
            Budget = DocumentLoadBudget.Default with
            {
                MaxMediaBytes = pngBytes.Length,
                MaxTotalMediaBytes = pngBytes.Length,
            },
        };

        Assert.Single(RtfReader.Load(rtfWithOnePng, exactPartLimit).Document.Media);

        Assert.Throws<RtfFormatException>(() => RtfReader.Load(rtfWithOnePng, new RtfImportOptions
        {
            Budget = DocumentLoadBudget.Default with { MaxMediaBytes = pngBytes.Length - 1 },
        }));

        Assert.Throws<RtfFormatException>(() => RtfReader.Load(rtfWithOnePng, new RtfImportOptions
        {
            Budget = DocumentLoadBudget.Default with { MaxTotalMediaBytes = pngBytes.Length - 1 },
        }));
    }

    private static byte[] RepeatedRtf(string token, int count)
    {
        var builder = new StringBuilder(checked(13 + (token.Length * count)));
        builder.Append(@"{\rtf1\ansi ");
        for (int index = 0; index < count; index++)
            builder.Append(token);
        builder.Append('}');
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private sealed class CancelBeforeEofStream(byte[] content, Action cancel) : Stream
    {
        private bool _contentRead;

        public int ReadCount { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            if (!_contentRead)
            {
                _contentRead = true;
                content.CopyTo(buffer.Span);
                return ValueTask.FromResult(content.Length);
            }

            cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(0);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
    }
}
