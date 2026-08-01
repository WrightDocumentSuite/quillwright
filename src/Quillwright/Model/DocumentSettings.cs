using System.Globalization;
using Quillwright.Primitives;

namespace Quillwright.Model;

/// <summary>
/// The settings part of a document (<c>settings.xml</c>).
/// </summary>
/// <remarks>
/// <c>CT_Settings</c> has around ninety optional children in a fixed order, almost all of
/// them switches no API should have to surface. Rather than model them one by one, the part
/// is held as its elements in document order: typed properties read and write the handful
/// that matter, everything else survives untouched, and a newly written setting is placed at
/// its schema position so the file stays valid.
/// </remarks>
public sealed class DocumentSettings
{
    private static readonly string[] SchemaOrder =
    [
        "writeProtection", "view", "zoom", "removePersonalInformation", "removeDateAndTime",
        "doNotDisplayPageBoundaries", "displayBackgroundShape", "printPostScriptOverText",
        "printFractionalCharacterWidth", "printFormsData", "embedTrueTypeFonts", "embedSystemFonts",
        "saveSubsetFonts", "saveFormsData", "mirrorMargins", "alignBordersAndEdges", "bordersDoNotSurroundHeader",
        "bordersDoNotSurroundFooter", "gutterAtTop", "hideSpellingErrors", "hideGrammaticalErrors",
        "activeWritingStyle", "proofState", "formsDesign", "attachedTemplate", "linkStyles",
        "stylePaneFormatFilter", "stylePaneSortMethod", "documentType", "mailMerge", "revisionView",
        "trackChanges", "doNotTrackMoves", "doNotTrackFormatting", "documentProtection", "autoFormatOverride",
        "styleLockTheme", "styleLockQFSet", "defaultTabStop", "autoHyphenation", "consecutiveHyphenLimit",
        "hyphenationZone", "doNotHyphenateCaps", "showEnvelope", "summaryLength", "clickAndTypeStyle",
        "defaultTableStyle", "evenAndOddHeaders", "bookFoldRevPrinting", "bookFoldPrinting",
        "bookFoldPrintingSheets", "drawingGridHorizontalSpacing", "drawingGridVerticalSpacing",
        "displayHorizontalDrawingGridEvery", "displayVerticalDrawingGridEvery", "doNotUseMarginsForDrawingGridOrigin",
        "drawingGridHorizontalOrigin", "drawingGridVerticalOrigin", "doNotShadeFormData", "noPunctuationKerning",
        "characterSpacingControl", "printTwoOnOne", "strictFirstAndLastChars", "noLineBreaksAfter",
        "noLineBreaksBefore", "savePreviewPicture", "doNotValidateAgainstSchema", "saveInvalidXml",
        "ignoreMixedContent", "alwaysShowPlaceholderText", "doNotDemarcateInvalidXml", "saveXmlDataOnly",
        "useXSLTWhenSaving", "saveThroughXslt", "showXMLTags", "alwaysMergeEmptyNamespace", "updateFields",
        "hdrShapeDefaults", "footnotePr", "endnotePr", "compat", "docVars", "rsids", "attachedSchema",
        "themeFontLang", "clrSchemeMapping", "doNotIncludeSubdocsInStats", "doNotAutoCompressPictures",
        "forceUpgrade", "captions", "readModeInkLockDown", "smartTagType", "shapeDefaults",
        "doNotEmbedSmartTags", "decimalSymbol", "listSeparator",
    ];

    private readonly List<(string Name, string Xml)> _elements = [];
    private DocumentVariableCollection? _variables;

    /// <summary>Attributes of <c>w:settings</c> this version does not model, kept verbatim.</summary>
    public string? Attributes { get; set; }

    /// <summary>The elements of the part in document order.</summary>
    internal IReadOnlyList<(string Name, string Xml)> Elements => _elements;

    /// <summary>Distance between the default tab stops (<c>w:defaultTabStop</c>).</summary>
    public Length DefaultTabStop
    {
        get => Length.FromTwips(GetInt("defaultTabStop") ?? 720);
        set => SetSimple("defaultTabStop", value.Twips.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Whether odd and even pages have different headers and footers (<c>w:evenAndOddHeaders</c>).</summary>
    public bool EvenAndOddHeaders
    {
        get => GetToggle("evenAndOddHeaders");
        set => SetToggle("evenAndOddHeaders", value);
    }

    /// <summary>Whether edits are recorded as tracked changes (<c>w:trackChanges</c>).</summary>
    public bool TrackRevisions
    {
        get => GetToggle("trackChanges");
        set => SetToggle("trackChanges", value);
    }

    /// <summary>Whether the consumer refreshes fields when the document opens (<c>w:updateFields</c>).</summary>
    public bool UpdateFieldsOnOpen
    {
        get => GetToggle("updateFields");
        set => SetToggle("updateFields", value);
    }

    /// <summary>Whether words are hyphenated automatically (<c>w:autoHyphenation</c>).</summary>
    public bool AutoHyphenation
    {
        get => GetToggle("autoHyphenation");
        set => SetToggle("autoHyphenation", value);
    }

    /// <summary>Whether words in capitals are exempt from automatic hyphenation (<c>w:doNotHyphenateCaps</c>).</summary>
    public bool DoNotHyphenateCaps
    {
        get => GetToggle("doNotHyphenateCaps");
        set => SetToggle("doNotHyphenateCaps", value);
    }

    /// <summary>
    /// The editing restrictions the document asks a consumer to honour
    /// (<c>w:documentProtection</c>), or <see langword="null"/> when it asks for none.
    /// </summary>
    /// <remarks>
    /// Reading gives a snapshot. Change it and assign it back to change the document; the
    /// attributes this version does not model travel with the snapshot and go back untouched.
    /// </remarks>
    public DocumentProtectionSettings? Protection
    {
        get => DocumentProtectionSettings.Parse(GetRaw("documentProtection"));
        set
        {
            if (value is null)
                Remove("documentProtection");
            else
                SetRaw("documentProtection", value.ToXml());
        }
    }

    /// <summary>Style applied to new tables (<c>w:defaultTableStyle</c>).</summary>
    public string? DefaultTableStyle
    {
        get => GetValue("defaultTableStyle");
        set
        {
            if (value is null)
                Remove("defaultTableStyle");
            else
                SetSimple("defaultTableStyle", value);
        }
    }

    /// <summary>
    /// The named values a document carries for its own use (<c>w:docVars</c>, §17.15.1.32),
    /// which a <c>DOCVARIABLE</c> field reads back.
    /// </summary>
    /// <remarks>
    /// Unlike a custom property, a document variable is invisible to a reader and is set by a
    /// macro rather than by the properties dialog, so it is where a template keeps the state
    /// it fills itself in from.
    /// </remarks>
    public DocumentVariableCollection Variables => _variables ??= new DocumentVariableCollection(this);

    /// <summary>
    /// How the document prints and numbers its footnotes (<c>w:footnotePr</c>). A section may
    /// override it; the element itself is kept verbatim and this reads it.
    /// </summary>
    public NoteProperties Footnotes => Formats.NotePropertiesReader.Parse(GetRaw("footnotePr"), endnotes: false);

    /// <summary>How the document prints and numbers its endnotes (<c>w:endnotePr</c>).</summary>
    public NoteProperties Endnotes => Formats.NotePropertiesReader.Parse(GetRaw("endnotePr"), endnotes: true);

    /// <summary>Reads a raw element by local name, or <see langword="null"/> when absent.</summary>
    /// <param name="name">Local name of the element.</param>
    public string? GetRaw(string name) => _elements.FirstOrDefault(e => e.Name == name).Xml;

    /// <summary>Writes a raw element by local name, replacing any existing one.</summary>
    /// <param name="name">Local name of the element.</param>
    /// <param name="xml">The complete element markup.</param>
    public void SetRaw(string name, string xml)
    {
        int index = _elements.FindIndex(e => e.Name == name);
        if (index >= 0)
            _elements[index] = (name, xml);
        else
            _elements.Insert(InsertionIndex(name), (name, xml));
    }

    /// <summary>Removes an element by local name.</summary>
    /// <param name="name">Local name of the element.</param>
    public bool Remove(string name) => _elements.RemoveAll(e => e.Name == name) > 0;

    internal void Append(string name, string xml) => _elements.Add((name, xml));

    internal void Clear() => _elements.Clear();

    private bool GetToggle(string name)
    {
        string? value = GetValue(name);
        return GetRaw(name) is not null && value is not ("0" or "false" or "off");
    }

    private void SetToggle(string name, bool value)
    {
        if (value)
            SetRaw(name, $"<w:{name}/>");
        else
            Remove(name);
    }

    private int? GetInt(string name) =>
        int.TryParse(GetValue(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : null;

    private string? GetValue(string name)
    {
        if (GetRaw(name) is not { } xml)
            return null;

        int marker = xml.IndexOf("w:val=\"", StringComparison.Ordinal);
        if (marker < 0)
            return null;

        int start = marker + "w:val=\"".Length;
        int end = xml.IndexOf('"', start);
        return end < 0 ? null : xml[start..end];
    }

    private void SetSimple(string name, string value) =>
        SetRaw(name, $"<w:{name} w:val=\"{System.Security.SecurityElement.Escape(value)}\"/>");

    private int InsertionIndex(string name)
    {
        int position = Array.IndexOf(SchemaOrder, name);
        if (position < 0)
            return _elements.Count;

        for (int i = 0; i < _elements.Count; i++)
        {
            int other = Array.IndexOf(SchemaOrder, _elements[i].Name);
            if (other < 0 || other > position)
                return i;
        }

        return _elements.Count;
    }
}
