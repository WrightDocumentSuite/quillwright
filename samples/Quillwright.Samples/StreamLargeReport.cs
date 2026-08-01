using System.Diagnostics;
using Quillwright.Model;
using Quillwright.Streaming;
using Quillwright.Styles;

namespace Quillwright.Samples;

/// <summary>
/// Generates a large report without building a model of it. Memory tracks the current
/// paragraph rather than the size of the document.
/// </summary>
internal static class StreamLargeReport
{
    public static async Task RunAsync(string directory)
    {
        string path = Path.Combine(directory, "05-large-report.docx");
        var stopwatch = Stopwatch.StartNew();
        long before = GC.GetTotalAllocatedBytes();

        DocxWriter writer = await DocxWriter.CreateAsync(path);
        await using (writer)
        {
            writer.Styles.GetOrAdd("Heading1");
            writer.WriteParagraph("Transactions", styleId: "Heading1");

            for (int i = 1; i <= 100_000; i++)
            {
                writer.WriteParagraph(
                    $"{i:D6}\tAccount {i % 997:D3}\t{i * 13.75m:N2}",
                    RunFormat.Default with { FontAscii = "Consolas" });
                await writer.FlushIfNeededAsync();
            }
        }

        long allocated = GC.GetTotalAllocatedBytes() - before;
        Console.WriteLine(
            $"  streamed {Path.GetFileName(path)}: 100 000 paragraphs in {stopwatch.ElapsedMilliseconds} ms, " +
            $"{allocated / (1024 * 1024)} MB allocated, {new FileInfo(path).Length / 1024} KB on disk");
    }
}
