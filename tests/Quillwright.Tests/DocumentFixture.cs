using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>Helpers every test set uses: save to memory, and save-then-load round trips.</summary>
internal static class DocumentFixture
{
    /// <summary>Saves a document to a rewound memory stream.</summary>
    public static async Task<MemoryStream> SaveAsync(WordDocument document)
    {
        var buffer = new MemoryStream();
        await document.SaveAsync(buffer);
        buffer.Position = 0;
        return buffer;
    }

    /// <summary>Saves a document, checks the package validates, and reads it back.</summary>
    public static async Task<WordDocument> RoundTripAsync(WordDocument document, string because = "round trip")
    {
        using MemoryStream buffer = await SaveAsync(document);
        OpenXmlAssert.Valid(buffer, because);
        buffer.Position = 0;
        return await WordDocument.LoadAsync(buffer);
    }
}
