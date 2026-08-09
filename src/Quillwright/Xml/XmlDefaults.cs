using System.Xml;
using Quillwright.Diagnostics;

namespace Quillwright.Xml;

/// <summary>
/// Hardened <see cref="XmlReader"/> settings shared by all parsing code paths.
/// </summary>
internal static class XmlDefaults
{
    /// <summary>Settings for synchronous parsing of buffered part content.</summary>
    public static readonly XmlReaderSettings ReaderSettings = Create(async: false);

    /// <summary>Settings for asynchronous streaming parsing of large parts.</summary>
    public static readonly XmlReaderSettings AsyncReaderSettings = Create(async: true);

    /// <summary>Creates synchronous settings with a per-document character ceiling.</summary>
    public static XmlReaderSettings ForBudget(DocumentLoadBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        budget.Validate();
        XmlReaderSettings settings = Create(async: false);
        settings.MaxCharactersInDocument = budget.MaxXmlCharactersPerPart;
        return settings;
    }

    private static XmlReaderSettings Create(bool async) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,

        // Word keeps significant whitespace in w:t elements marked xml:space="preserve";
        // dropping whitespace nodes wholesale would silently eat spaces between runs.
        IgnoreWhitespace = false,
        CloseInput = false,
        Async = async,
    };
}
