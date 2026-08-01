namespace Quillwright.Formats;

/// <summary>
/// The vocabulary of a WordprocessingML package: namespaces, content types, relationship
/// types and the conventional part names. Kept in one place so the reader, the writer and
/// the preservation layer agree on every string.
/// </summary>
internal static class DocxSchema
{
    /// <summary>Transitional main WordprocessingML namespace.</summary>
    public const string NsWord = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>Strict main WordprocessingML namespace, normalized to <see cref="NsWord"/> on read.</summary>
    public const string NsWordStrict = "http://purl.oclc.org/ooxml/wordprocessingml/main";

    public const string NsRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public const string NsRelationshipsStrict = "http://purl.oclc.org/ooxml/officeDocument/relationships";
    public const string NsPackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    public const string NsDrawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    public const string NsWordDrawing = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    public const string NsPicture = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    public const string NsChart = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    public const string NsChartStrict = "http://purl.oclc.org/ooxml/drawingml/chart";
    public const string NsWordShape = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    public const string NsMath = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    /// <summary>Strict Office Math namespace, which names the same elements as <see cref="NsMath"/>.</summary>
    public const string NsMathStrict = "http://purl.oclc.org/ooxml/officeDocument/math";
    public const string NsMarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    public const string NsVml = "urn:schemas-microsoft-com:vml";
    public const string NsOffice = "urn:schemas-microsoft-com:office:office";
    public const string NsWord10 = "urn:schemas-microsoft-com:office:word";
    public const string NsXml = "http://www.w3.org/XML/1998/namespace";

    /// <summary>Word 2010 extensions (paragraph/run ids, content control appearance).</summary>
    public const string NsW14 = "http://schemas.microsoft.com/office/word/2010/wordml";

    /// <summary>Word 2012 extensions: comment threading (<c>w15:paraId</c>, repeating sections).</summary>
    public const string NsW15 = "http://schemas.microsoft.com/office/word/2012/wordml";

    /// <summary>Word 2016 comment identifiers, see [MS-DOCX] 2.8.</summary>
    public const string NsW16Cid = "http://schemas.microsoft.com/office/word/2016/wordml/cid";

    /// <summary>Word 2018 extension lists, see [MS-DOCX] 2.9.</summary>
    public const string NsW16 = "http://schemas.microsoft.com/office/word/2018/wordml";

    /// <summary>Word 2018 comment metadata, see [MS-DOCX] 2.10.</summary>
    public const string NsW16Cex = "http://schemas.microsoft.com/office/word/2018/wordml/cex";

    /// <summary>
    /// Comment reactions, see [MS-OREACTXML]. Only declared, never generated: a reaction is
    /// carried through inside the extension list of a <c>commentExtensible</c> entry.
    /// </summary>
    public const string NsReactions = "http://schemas.microsoft.com/office/comments/2020/reactions";

    /// <summary>Word 2015 symbol extensions, see [MS-DOCX] 2.7.</summary>
    public const string NsW16Se = "http://schemas.microsoft.com/office/word/2015/wordml/symex";

    /// <summary>Legacy Word numbering extensions.</summary>
    public const string NsWne = "http://schemas.microsoft.com/office/word/2006/wordml";

    /// <summary>Application properties, see ISO/IEC 29500-1 §22.2.</summary>
    public const string NsExtendedProperties = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";

    /// <summary>Custom properties, see ISO/IEC 29500-1 §22.3.</summary>
    public const string NsCustomProperties = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";

    /// <summary>The variant types a property value is written in, see ISO/IEC 29500-1 §22.4.</summary>
    public const string NsVariantTypes = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";

    public const string ContentTypeRelationships = "application/vnd.openxmlformats-package.relationships+xml";
    public const string ContentTypeDocument = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    public const string ContentTypeTemplate = "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml";
    public const string ContentTypeMacroDocument = "application/vnd.ms-word.document.macroEnabled.main+xml";
    public const string ContentTypeMacroTemplate = "application/vnd.ms-word.template.macroEnabledTemplate.main+xml";
    public const string ContentTypeStyles = "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml";
    public const string ContentTypeNumbering = "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml";
    public const string ContentTypeSettings = "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml";
    public const string ContentTypeWebSettings = "application/vnd.openxmlformats-officedocument.wordprocessingml.webSettings+xml";
    public const string ContentTypeFontTable = "application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml";
    public const string ContentTypeTheme = "application/vnd.openxmlformats-officedocument.theme+xml";
    public const string ContentTypeHeader = "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml";
    public const string ContentTypeFooter = "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml";
    public const string ContentTypeFootnotes = "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml";
    public const string ContentTypeEndnotes = "application/vnd.openxmlformats-officedocument.wordprocessingml.endnotes+xml";
    public const string ContentTypeComments = "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml";
    public const string ContentTypeCommentsExtended = "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtended+xml";
    public const string ContentTypeCommentsIds = "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsIds+xml";
    public const string ContentTypeCommentsExtensible = "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtensible+xml";
    public const string ContentTypePeople = "application/vnd.openxmlformats-officedocument.wordprocessingml.people+xml";
    public const string ContentTypeCoreProperties = "application/vnd.openxmlformats-package.core-properties+xml";
    public const string ContentTypeExtendedProperties = "application/vnd.openxmlformats-officedocument.extended-properties+xml";
    public const string ContentTypeCustomProperties = "application/vnd.openxmlformats-officedocument.custom-properties+xml";
    public const string ContentTypeChart = "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";

    public const string RelDocument = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    public const string RelStyles = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    public const string RelNumbering = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering";
    public const string RelSettings = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings";
    public const string RelWebSettings = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/webSettings";
    public const string RelFontTable = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable";
    public const string RelTheme = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";
    public const string RelHeader = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header";
    public const string RelFooter = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer";
    public const string RelFootnotes = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes";
    public const string RelEndnotes = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/endnotes";
    public const string RelComments = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
    public const string RelImage = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    public const string RelHyperlink = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
    public const string RelGlossaryDocument = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/glossaryDocument";
    public const string RelCoreProperties = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
    public const string RelExtendedProperties = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties";
    public const string RelCustomProperties = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties";
    public const string RelCommentsExtended = "http://schemas.microsoft.com/office/2011/relationships/commentsExtended";
    public const string RelPeople = "http://schemas.microsoft.com/office/2011/relationships/people";
    public const string RelCommentsIds = "http://schemas.microsoft.com/office/2016/09/relationships/commentsIds";
    public const string RelCommentsExtensible = "http://schemas.microsoft.com/office/2018/08/relationships/commentsExtensible";
    public const string RelVbaProject = "http://schemas.microsoft.com/office/2006/relationships/vbaProject";
    public const string RelTaskPanes = "http://schemas.microsoft.com/office/2011/relationships/webextensiontaskpanes";
    public const string RelWebExtension = "http://schemas.microsoft.com/office/2011/relationships/webextension";
    public const string RelSignatureOrigin = "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/origin";
    public const string RelSignature = "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/signature";
    public const string RelSignatureCertificate = "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/certificate";
    public const string RelOleObject = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject";
    public const string RelPackage = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package";

    public const string PartDocument = "/word/document.xml";
    public const string PartStyles = "/word/styles.xml";
    public const string PartNumbering = "/word/numbering.xml";
    public const string PartSettings = "/word/settings.xml";
    public const string PartFontTable = "/word/fontTable.xml";
    public const string PartTheme = "/word/theme/theme1.xml";
    public const string PartFootnotes = "/word/footnotes.xml";
    public const string PartEndnotes = "/word/endnotes.xml";
    public const string PartComments = "/word/comments.xml";
    public const string PartCommentsExtended = "/word/commentsExtended.xml";
    public const string PartCommentsIds = "/word/commentsIds.xml";
    public const string PartCommentsExtensible = "/word/commentsExtensible.xml";
    public const string PartPeople = "/word/people.xml";
    public const string PartCoreProperties = "/docProps/core.xml";
    public const string PartExtendedProperties = "/docProps/app.xml";
    public const string PartCustomProperties = "/docProps/custom.xml";

    /// <summary>
    /// The prefixes <see cref="RootNamespaces"/> declares for extension vocabularies, listed
    /// as the value of <c>mc:Ignorable</c> for consumers that do not know them.
    /// </summary>
    public static ReadOnlySpan<byte> IgnorablePrefixes => " mc:Ignorable=\"w14 w15 w16se w16cid wp14\""u8;

    /// <summary>The full namespace declaration block Word writes on every root element we generate.</summary>
    public static ReadOnlySpan<byte> RootNamespaces =>
        " xmlns:wpc=\"http://schemas.microsoft.com/office/word/2010/wordprocessingCanvas\""u8 +
        " xmlns:cx=\"http://schemas.microsoft.com/office/drawing/2014/chartex\""u8 +
        " xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\""u8 +
        " xmlns:o=\"urn:schemas-microsoft-com:office:office\""u8 +
        " xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\""u8 +
        " xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\""u8 +
        " xmlns:v=\"urn:schemas-microsoft-com:vml\""u8 +
        " xmlns:wp14=\"http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing\""u8 +
        " xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\""u8 +
        " xmlns:w10=\"urn:schemas-microsoft-com:office:word\""u8 +
        " xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\""u8 +
        " xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\""u8 +
        " xmlns:w15=\"http://schemas.microsoft.com/office/word/2012/wordml\""u8 +
        " xmlns:w16cid=\"http://schemas.microsoft.com/office/word/2016/wordml/cid\""u8 +
        " xmlns:w16se=\"http://schemas.microsoft.com/office/word/2015/wordml/symex\""u8 +
        " xmlns:wpg=\"http://schemas.microsoft.com/office/word/2010/wordprocessingGroup\""u8 +
        " xmlns:wpi=\"http://schemas.microsoft.com/office/word/2010/wordprocessingInk\""u8 +
        " xmlns:wne=\"http://schemas.microsoft.com/office/word/2006/wordml\""u8 +
        " xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\""u8;

    /// <summary>
    /// The roles whose two spellings differ by more than the base they hang off: the property
    /// parts are hyphenated in the Transitional vocabulary and camel-cased in the Strict one,
    /// so swapping the base alone would leave a Strict package with two of each.
    /// </summary>
    private static readonly (string Canonical, string Strict)[] RenamedRelationships =
    [
        (RelExtendedProperties, NsRelationshipsStrict + "/extendedProperties"),
        (RelCustomProperties, NsRelationshipsStrict + "/customProperties"),
    ];

    /// <summary>
    /// Maps a Strict-namespace URI onto its Transitional twin. A Strict package names the same
    /// roles under purl.oclc.org; normalizing on read means every consumer above works in one
    /// vocabulary, and the package we write is Transitional throughout rather than half of each.
    /// </summary>
    public static string Canonical(string uri)
    {
        foreach ((string canonical, string strict) in RenamedRelationships)
        {
            if (uri == strict)
                return canonical;
        }

        return uri switch
        {
            NsWordStrict => NsWord,
            NsRelationshipsStrict => NsRelationships,
            _ when uri.StartsWith(NsRelationshipsStrict + "/", StringComparison.Ordinal) =>
                string.Concat(NsRelationships, "/", uri.AsSpan((NsRelationshipsStrict + "/").Length)),
            _ => uri,
        };
    }

    /// <summary>The Strict spelling of a relationship type, for a package that uses that vocabulary.</summary>
    public static string ToStrict(string canonicalType)
    {
        foreach ((string canonical, string strict) in RenamedRelationships)
        {
            if (canonicalType == canonical)
                return strict;
        }

        return canonicalType.StartsWith(NsRelationships + "/", StringComparison.Ordinal)
            ? string.Concat(NsRelationshipsStrict, "/", canonicalType.AsSpan((NsRelationships + "/").Length))
            : canonicalType;
    }

    /// <summary>Returns <see langword="true"/> for both spellings of the main WordprocessingML namespace.</summary>
    public static bool IsWordNamespace(string? uri) => uri is NsWord or NsWordStrict;

    /// <summary>Returns <see langword="true"/> for both spellings of the Office Math namespace.</summary>
    public static bool IsMathNamespace(string? uri) => uri is NsMath or NsMathStrict;
}
