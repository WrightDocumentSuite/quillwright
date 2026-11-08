using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Quillwright.Diagnostics;
using Quillwright.Doc;
using Quillwright.Html;
using Quillwright.IO;
using Quillwright.Markdown;
using Quillwright.Model;
using Quillwright.Rtf;
using SharpFuzz;

namespace Quillwright.Fuzz;

internal static class Program
{
    private static readonly DocumentLoadBudget FuzzBudget = new()
    {
        MaxInputBytes = 1024 * 1024,
        MaxPackageParts = 2_048,
        MaxInflatedBytes = 32 * 1024 * 1024,
        MaxPartBytes = 8 * 1024 * 1024,
        MaxXmlCharactersPerPart = 8 * 1024 * 1024,
        MaxXmlNodes = 1_000_000,
        MaxXmlDepth = 128,
        MaxTextCharacters = 1_000_000,
        MaxMarkupNodes = 100_000,
        MaxMarkupDepth = 128,
        MaxLines = 100_000,
        MaxMediaBytes = 4 * 1024 * 1024,
        MaxTotalMediaBytes = 8 * 1024 * 1024,
        MaxEmbeddedObjectBytes = 4 * 1024 * 1024,
        MaxEmbeddedObjects = 64,
    };

    private static readonly IReadOnlyDictionary<string, Action<ReadOnlyMemory<byte>>> Targets =
        new Dictionary<string, Action<ReadOnlyMemory<byte>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["docx"] = ReadDocx,
            ["doc"] = ReadDoc,
            ["rtf"] = ReadRtf,
            ["html"] = ReadHtml,
            ["markdown"] = ReadMarkdown,
        };

    private static int Main(string[] args)
    {
        if (args.Length == 0 || !Targets.TryGetValue(args[0], out Action<ReadOnlyMemory<byte>>? target))
        {
            Console.Error.WriteLine("Usage: Quillwright.Fuzz <docx|doc|rtf|html|markdown> [--replay <input>]");
            return 2;
        }

        if (args.Length == 3 && args[1].Equals("--replay", StringComparison.OrdinalIgnoreCase))
        {
            target(File.ReadAllBytes(args[2]));
            return 0;
        }

        if (args.Length != 1)
        {
            Console.Error.WriteLine("Pass either a target alone or '<target> --replay <input>'.");
            return 2;
        }

        Fuzzer.OutOfProcess.Run(stream => target(ReadAll(stream)));
        return 0;
    }

    private static byte[] ReadAll(Stream input)
    {
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void ReadDocx(ReadOnlyMemory<byte> input)
    {
        using var stream = new MemoryStream(input.ToArray(), writable: false);
        try
        {
            WordDocument doc = WordDocument.LoadAsync(stream, new LoadOptions { Budget = FuzzBudget })
                .AsTask()
                .GetAwaiter()
                .GetResult();
            _ = doc.GetText();
        }
        catch (Exception exception) when (IsRejectedInput(exception))
        {
        }
    }

    private static void ReadDoc(ReadOnlyMemory<byte> input)
    {
        try
        {
            WordDocument doc = DocReader.LoadWithOptions(
                input.ToArray(),
                new DocImportOptions { Password = "password", Budget = FuzzBudget });
            _ = doc.GetText();
        }
        catch (Exception exception) when (IsRejectedInput(exception))
        {
        }
    }

    private static void ReadRtf(ReadOnlyMemory<byte> input)
    {
        try
        {
            RtfImportResult result = RtfReader.Load(input.Span, new RtfImportOptions { Budget = FuzzBudget });
            _ = result.Document.GetText();
        }
        catch (Exception exception) when (IsRejectedInput(exception))
        {
        }
    }

    private static void ReadHtml(ReadOnlyMemory<byte> input)
    {
        try
        {
            string html = Encoding.UTF8.GetString(input.Span);
            HtmlImportResult result = HtmlImporter.Import(html, new HtmlImportOptions { Budget = FuzzBudget });
            _ = result.Document.GetText();
        }
        catch (Exception exception) when (IsRejectedInput(exception))
        {
        }
    }

    private static void ReadMarkdown(ReadOnlyMemory<byte> input)
    {
        try
        {
            string markdown = Encoding.UTF8.GetString(input.Span);
            MarkdownImportResult result = MarkdownImporter.Import(markdown, new MarkdownImportOptions { Budget = FuzzBudget });
            _ = result.Document.GetText();
        }
        catch (Exception exception) when (IsRejectedInput(exception))
        {
        }
    }

    private static bool IsRejectedInput(Exception exception) => exception switch
    {
        DocFormatException or RtfFormatException or DocumentLoadLimitException or EncryptedDocumentException => true,
        InvalidDataException or IOException or NotSupportedException => true,
        XmlException or CryptographicException => true,
        ArgumentException when exception.GetType() == typeof(ArgumentException) => true,
        _ => false,
    };
}
