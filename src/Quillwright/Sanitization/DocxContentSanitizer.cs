using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Quillwright.Sanitization;

/// <summary>The parts of sanitizing that depend on WordprocessingML.</summary>
internal sealed partial class DocxContentSanitizer(ContentCategories remove) : OpcContentSanitizer(remove)
{
    private static readonly HashSet<string> Prunable = new(StringComparer.Ordinal);

    protected override IReadOnlySet<string> PruneWhenEmpty => Prunable;

    protected override string? PlainContentType(string contentType) => contentType switch
    {
        "application/vnd.ms-word.document.macroEnabled.main+xml" =>
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
        "application/vnd.ms-word.template.macroEnabledTemplate.main+xml" =>
            "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml",
        _ => null,
    };

    /// <summary>
    /// A hyperlink wraps text that has to stay, and an embedded object is a whole
    /// <c>w:object</c> whose preview shape should not outlive it.
    /// </summary>
    protected override void RemoveReference(XElement element, ReferenceScope scope)
    {
        switch (element.Name.LocalName)
        {
            case "hyperlink":
                scope.Unwrap(element);
                return;

            case "OLEObject" or "control":
                scope.Remove(element.Ancestors().FirstOrDefault(static a => a.Name.LocalName == "object") ?? element);
                return;

            default:
                scope.Remove(element);
                return;
        }
    }

    protected override void ApplyFormatRules()
    {
        foreach (string path in PartPaths.Where(static p => p.StartsWith("/word/", StringComparison.OrdinalIgnoreCase) && IsXmlPart(p)).ToArray())
        {
            string text = ReadText(path);

            if ((Selected & ContentCategories.CustomXml) != 0 && text.Contains("dataBinding", StringComparison.Ordinal))
                RemoveDataBindings(path);

            if (text.Contains("instr", StringComparison.Ordinal))
                ReportExternalFields(path);
        }
    }

    /// <summary>A content control bound to a data part that has gone would read from nothing.</summary>
    private void RemoveDataBindings(string path)
    {
        XDocument document = ReadXml(path);
        XElement[] bindings = [.. document.Descendants().Where(static e => e.Name.LocalName == "dataBinding")];
        foreach (XElement binding in bindings)
        {
            binding.Remove();
            ReportRemoved(ContentCategories.CustomXml, SanitizedItemKind.Markup, path, "dataBinding");
        }

        if (bindings.Length > 0)
            WriteXml(path, document);
    }

    /// <summary>
    /// Fields name their targets in text rather than through a relationship. Taking one out means
    /// deciding what its result becomes, so they are reported for a person to deal with.
    /// </summary>
    private void ReportExternalFields(string path)
    {
        foreach (XElement element in ReadXml(path).Descendants())
        {
            string? instruction = element.Name.LocalName switch
            {
                "instrText" => element.Value,
                "fldSimple" => element.Attributes().FirstOrDefault(static a => a.Name.LocalName == "instr")?.Value,
                _ => null,
            };

            if (instruction is not null && ExternalField().IsMatch(instruction))
            {
                ReportRetained(
                    ContentCategories.ExternalLinks,
                    SanitizedItemKind.FieldInstruction,
                    path,
                    instruction.Trim());
            }
        }
    }

    [GeneratedRegex(@"^\s*(INCLUDETEXT|INCLUDEPICTURE|LINK|DDE|DDEAUTO|HYPERLINK)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ExternalField();
}
