using System.IO.Compression;
using System.Text;
using Xunit;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// Rewrites a saved package to hold something the model cannot yet author.
/// </summary>
/// <remarks>
/// A chart is read but never written, so a document with one cannot be built through the API.
/// Splicing the part, its relationship, its content type and the drawing that references it into
/// a package the library itself produced is the only way to test reading and drawing one — and
/// it also exercises exactly the join a real document has.
/// </remarks>
internal static class Packages
{
    private const string ChartContentType = "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
    private const string ChartRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";

    /// <summary>Adds a chart part and a drawing that reserves room for it.</summary>
    /// <param name="package">A package the library saved.</param>
    /// <param name="partPath">Absolute name of the chart part to add.</param>
    /// <param name="chart">The chart part's markup.</param>
    /// <param name="frame">The drawing to append to the body, without its run wrapper.</param>
    public static byte[] With(byte[] package, string partPath, string chart, string frame)
    {
        Dictionary<string, byte[]> entries = Read(package);
        string entry = partPath.TrimStart('/');

        entries[entry] = Encoding.UTF8.GetBytes(chart);
        entries["[Content_Types].xml"] = Insert(
            entries["[Content_Types].xml"],
            "</Types>",
            $"<Override PartName=\"{partPath}\" ContentType=\"{ChartContentType}\"/>");

        entries["word/_rels/document.xml.rels"] = Insert(
            entries["word/_rels/document.xml.rels"],
            "</Relationships>",
            $"<Relationship Id=\"rIdChart\" Type=\"{ChartRelationship}\" Target=\"{Relative(entry)}\"/>");

        entries["word/document.xml"] = Insert(entries["word/document.xml"], "<w:sectPr", "<w:p><w:r>" + frame + "</w:r></w:p>");
        return Write(entries);
    }

    /// <summary>The text of one part of a package.</summary>
    /// <param name="package">The package.</param>
    /// <param name="entry">Name of the entry, without a leading slash.</param>
    public static string Part(byte[] package, string entry) => Encoding.UTF8.GetString(Read(package)[entry]);

    /// <summary>The chart's target as document.xml names it, which is relative to <c>word/</c>.</summary>
    private static string Relative(string entry) =>
        entry.StartsWith("word/", StringComparison.Ordinal) ? entry["word/".Length..] : "/" + entry;

    private static byte[] Insert(byte[] content, string before, string markup)
    {
        string text = Encoding.UTF8.GetString(content);
        int at = text.IndexOf(before, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the part does not contain '{before}'");
        return Encoding.UTF8.GetBytes(text[..at] + markup + text[at..]);
    }

    private static Dictionary<string, byte[]> Read(byte[] package)
    {
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            using Stream stream = entry.Open();
            var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            entries[entry.FullName] = buffer.ToArray();
        }

        return entries;
    }

    private static byte[] Write(Dictionary<string, byte[]> entries)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in entries)
            {
                using Stream stream = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }
}
