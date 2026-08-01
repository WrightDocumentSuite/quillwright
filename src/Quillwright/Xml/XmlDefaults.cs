using System.Xml;

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
