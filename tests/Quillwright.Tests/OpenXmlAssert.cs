using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace Quillwright.Tests;

/// <summary>
/// Checks a package Quillwright produced against the Open XML SDK.
/// </summary>
/// <remarks>
/// The SDK is a test-only dependency used as an oracle: it enforces the ISO-29500 schema,
/// including the element ordering that is easy to get subtly wrong, and it opens the package
/// the way Word does.
/// </remarks>
internal static class OpenXmlAssert
{
    /// <summary>Fails when the package does not validate against the Word 2019 schema.</summary>
    public static void Valid(Stream package, string because)
    {
        package.Position = 0;
        using WordprocessingDocument document = WordprocessingDocument.Open(package, isEditable: false);
        var validator = new OpenXmlValidator(FileFormatVersions.Office2019);
        List<ValidationErrorInfo> errors = [.. validator.Validate(document).Where(IsNotTheSdksOwnGap)];
        if (errors.Count == 0)
            return;

        string report = string.Join(
            Environment.NewLine,
            errors.Take(15).Select(e => $"{e.ErrorType} at {e.Path?.XPath}: {e.Description}"));
        Assert.Fail($"{because}: {errors.Count} validation error(s).{Environment.NewLine}{report}");
    }

    /// <summary>
    /// Whether an error is the oracle's rather than ours: the SDK does not know that
    /// <c>w:lvlJc</c> takes the Strict spelling of its alignment.
    /// </summary>
    /// <remarks>
    /// ISO/IEC 29500-1 §17.9.7 types <c>lvlJc</c> as <c>CT_Jc</c>, whose Strict enumeration is
    /// <c>start</c>, <c>center</c>, <c>end</c> — and [MS-OI29500] 2.1.281(a) says those three
    /// are exactly what Word supports there. Word writes <c>start</c> accordingly: the Strict
    /// corpus has 29 of them against 30 of the Transitional spelling. The SDK's validator
    /// rejects the standard spelling all the same, and it rejects it in files Word wrote and
    /// nothing here has touched — <c>Strict01.docx</c> fails with 36 such errors when opened
    /// straight off disk. Suppressing exactly this error is therefore the only way to keep the
    /// oracle useful without writing markup the standard forbids.
    /// </remarks>
    private static bool IsNotTheSdksOwnGap(ValidationErrorInfo error) =>
        error.Path?.XPath?.Contains("lvlJc", StringComparison.Ordinal) != true ||
        (error.Description?.Contains("'start'", StringComparison.Ordinal) != true &&
         error.Description?.Contains("'end'", StringComparison.Ordinal) != true);

    /// <summary>Returns the text of the main document part as the Open XML SDK sees it.</summary>
    public static string ReadText(Stream package)
    {
        package.Position = 0;
        using WordprocessingDocument document = WordprocessingDocument.Open(package, isEditable: false);
        return document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
    }

    /// <summary>Returns the raw markup of a part, for assertions about what was written.</summary>
    public static string ReadPart(Stream package, string uri)
    {
        package.Position = 0;
        using WordprocessingDocument document = WordprocessingDocument.Open(package, isEditable: false);
        OpenXmlPart? part = document.MainDocumentPart?.Parts
            .Select(p => p.OpenXmlPart)
            .FirstOrDefault(p => p.Uri.ToString().EndsWith(uri, StringComparison.OrdinalIgnoreCase));

        if (part is null && document.MainDocumentPart?.Uri.ToString().EndsWith(uri, StringComparison.OrdinalIgnoreCase) == true)
            part = document.MainDocumentPart;

        using var reader = new StreamReader(Assert.IsAssignableFrom<OpenXmlPart>(part).GetStream());
        return reader.ReadToEnd();
    }
}
