using Quillwright.Doc;
using Quillwright.Model;
using Quillwright.Streaming;

namespace Quillwright.Samples;

/// <summary>
/// Pulls text out of documents without building the model, and takes a document through the
/// legacy <c>.doc</c> format and back.
/// </summary>
internal static class ExtractText
{
    public static async Task RunAsync(string directory)
    {
        string source = Path.Combine(directory, "05-large-report.docx");
        if (File.Exists(source))
        {
            var lines = 0;
            var characters = 0;
            DocxReader reader = await DocxReader.OpenAsync(source);
            await using (reader)
            {
                await foreach (string line in reader.ReadTextAsync())
                {
                    lines++;
                    characters += line.Length;
                }
            }

            Console.WriteLine($"  extracted {lines} line(s), {characters} character(s) without building the model");
        }

        await ConvertLegacyAsync(directory);
    }

    /// <summary>
    /// Writes a Word 97 file and reads it back. A sample that reached for a real <c>.doc</c>
    /// somewhere on the machine would only run on the machine that has one, so it makes its
    /// own: the writer and the reader are the two halves of the same claim anyway.
    /// </summary>
    private static async Task ConvertLegacyAsync(string directory)
    {
        WordDocument original = WordDocument.Create();
        original.Sections[0].AddParagraph("Minutes of the meeting", "Heading1");
        original.Sections[0].AddParagraph("Carried over from the previous quarter.");

        string legacy = Path.Combine(directory, "06-written-as-doc.doc");
        await DocWriter.SaveAsync(original, legacy);

        try
        {
            WordDocument converted = await DocReader.LoadAsync(legacy);
            string path = Path.Combine(directory, "06-converted-from-doc.docx");
            await converted.SaveAsync(path);
            Console.WriteLine($"  converted {Path.GetFileName(legacy)} to {Path.GetFileName(path)} " +
                              $"({converted.Paragraphs.Count()} paragraph(s))");
        }
        catch (DocFormatException error)
        {
            Console.WriteLine($"  legacy conversion refused: {error.Message}");
        }
    }
}
