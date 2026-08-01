using System.Globalization;

namespace Quillwright.Model;

/// <summary>
/// The application properties of a document (<c>docProps/app.xml</c>, ISO/IEC 29500-1 §22.2):
/// which program wrote it, the statistics it counted, and the company it belongs to.
/// </summary>
/// <remarks>
/// The part's children are a fixed sequence, and several of them — the heading pairs, the
/// titles of parts, a digital signature — are vectors no API should have to surface. So the
/// part is held as its elements in document order the way <see cref="DocumentSettings"/> is:
/// typed properties read and write the ones that matter, everything else survives untouched,
/// and a newly written element is placed at its schema position so the file stays valid.
/// </remarks>
public sealed class ExtendedProperties
{
    private static readonly string[] SchemaOrder =
    [
        "Template", "Manager", "Company", "Pages", "Words", "Characters", "PresentationFormat",
        "Lines", "Paragraphs", "Slides", "Notes", "TotalTime", "HiddenSlides", "MMClips",
        "ScaleCrop", "HeadingPairs", "TitlesOfParts", "LinksUpToDate", "CharactersWithSpaces",
        "SharedDoc", "HyperlinkBase", "HLinks", "HyperlinksChanged", "DigSig", "Application",
        "AppVersion", "DocSecurity",
    ];

    private readonly List<(string Name, string Xml)> _elements = [];

    /// <summary>Whether the part holds nothing at all.</summary>
    public bool IsEmpty => _elements.Count == 0;

    /// <summary>The elements of the part in document order.</summary>
    internal IReadOnlyList<(string Name, string Xml)> Elements => _elements;

    /// <summary>Name of the program that wrote the document (<c>Application</c>).</summary>
    public string? Application
    {
        get => GetText(nameof(Application));
        set => SetText(nameof(Application), value);
    }

    /// <summary>Version of that program (<c>AppVersion</c>), spelled <c>XX.YYYY</c>.</summary>
    public string? ApplicationVersion
    {
        get => GetText("AppVersion");
        set => SetText("AppVersion", value);
    }

    /// <summary>Company the document belongs to (<c>Company</c>).</summary>
    public string? Company
    {
        get => GetText(nameof(Company));
        set => SetText(nameof(Company), value);
    }

    /// <summary>Who manages the work the document is part of (<c>Manager</c>).</summary>
    public string? Manager
    {
        get => GetText(nameof(Manager));
        set => SetText(nameof(Manager), value);
    }

    /// <summary>Template the document was created from (<c>Template</c>).</summary>
    public string? Template
    {
        get => GetText(nameof(Template));
        set => SetText(nameof(Template), value);
    }

    /// <summary>Base every relative hyperlink is resolved against (<c>HyperlinkBase</c>).</summary>
    public string? HyperlinkBase
    {
        get => GetText(nameof(HyperlinkBase));
        set => SetText(nameof(HyperlinkBase), value);
    }

    /// <summary>Page count as the writing program last counted it (<c>Pages</c>).</summary>
    public int? Pages
    {
        get => GetInt(nameof(Pages));
        set => SetInt(nameof(Pages), value);
    }

    /// <summary>Word count as the writing program last counted it (<c>Words</c>).</summary>
    public int? Words
    {
        get => GetInt(nameof(Words));
        set => SetInt(nameof(Words), value);
    }

    /// <summary>Character count excluding spaces (<c>Characters</c>).</summary>
    public int? Characters
    {
        get => GetInt(nameof(Characters));
        set => SetInt(nameof(Characters), value);
    }

    /// <summary>Character count including spaces (<c>CharactersWithSpaces</c>).</summary>
    public int? CharactersWithSpaces
    {
        get => GetInt(nameof(CharactersWithSpaces));
        set => SetInt(nameof(CharactersWithSpaces), value);
    }

    /// <summary>Line count as the writing program last counted it (<c>Lines</c>).</summary>
    public int? Lines
    {
        get => GetInt(nameof(Lines));
        set => SetInt(nameof(Lines), value);
    }

    /// <summary>Paragraph count as the writing program last counted it (<c>Paragraphs</c>).</summary>
    public int? Paragraphs
    {
        get => GetInt(nameof(Paragraphs));
        set => SetInt(nameof(Paragraphs), value);
    }

    /// <summary>Minutes the document has been open for editing (<c>TotalTime</c>).</summary>
    public int? TotalEditingMinutes
    {
        get => GetInt("TotalTime");
        set => SetInt("TotalTime", value);
    }

    /// <summary>Which protections the document was saved with (<c>DocSecurity</c>).</summary>
    public int? DocumentSecurity
    {
        get => GetInt("DocSecurity");
        set => SetInt("DocSecurity", value);
    }

    /// <summary>Whether the linked content the document caches is current (<c>LinksUpToDate</c>).</summary>
    public bool? LinksUpToDate
    {
        get => GetBool(nameof(LinksUpToDate));
        set => SetBool(nameof(LinksUpToDate), value);
    }

    /// <summary>Whether the document is shared between several authors (<c>SharedDoc</c>).</summary>
    public bool? SharedDocument
    {
        get => GetBool("SharedDoc");
        set => SetBool("SharedDoc", value);
    }

    /// <summary>Whether a hyperlink changed since the list was last written (<c>HyperlinksChanged</c>).</summary>
    public bool? HyperlinksChanged
    {
        get => GetBool(nameof(HyperlinksChanged));
        set => SetBool(nameof(HyperlinksChanged), value);
    }

    /// <summary>Whether the thumbnail is scaled rather than cropped (<c>ScaleCrop</c>).</summary>
    public bool? ScaleCrop
    {
        get => GetBool(nameof(ScaleCrop));
        set => SetBool(nameof(ScaleCrop), value);
    }

    /// <summary>Reads an element by name, or <see langword="null"/> when absent.</summary>
    /// <param name="name">Name of the element.</param>
    public string? GetRaw(string name) => _elements.FirstOrDefault(e => e.Name == name).Xml;

    /// <summary>Writes an element by name, replacing any existing one and keeping schema order.</summary>
    /// <param name="name">Name of the element.</param>
    /// <param name="xml">The complete element markup.</param>
    public void SetRaw(string name, string xml)
    {
        int index = _elements.FindIndex(e => e.Name == name);
        if (index >= 0)
            _elements[index] = (name, xml);
        else
            _elements.Insert(InsertionIndex(name), (name, xml));
    }

    /// <summary>Removes an element by name.</summary>
    /// <param name="name">Name of the element.</param>
    public bool Remove(string name) => _elements.RemoveAll(e => e.Name == name) > 0;

    internal void Append(string name, string xml) => _elements.Add((name, xml));

    internal void Clear() => _elements.Clear();

    private string? GetText(string name) => Content(GetRaw(name));

    private void SetText(string name, string? value)
    {
        if (value is null)
            Remove(name);
        else
            SetRaw(name, $"<{name}>{System.Security.SecurityElement.Escape(value)}</{name}>");
    }

    private int? GetInt(string name) =>
        int.TryParse(GetText(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : null;

    private void SetInt(string name, int? value) =>
        SetText(name, value?.ToString(CultureInfo.InvariantCulture));

    private bool? GetBool(string name) => GetText(name) switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => null,
    };

    private void SetBool(string name, bool? value) => SetText(name, value is { } flag ? flag ? "true" : "false" : null);

    /// <summary>The text between the tags of a simple element.</summary>
    private static string? Content(string? xml)
    {
        if (xml is null)
            return null;

        int open = xml.IndexOf('>');
        int close = xml.LastIndexOf('<');
        return open < 0 || close <= open ? string.Empty : System.Net.WebUtility.HtmlDecode(xml[(open + 1)..close]);
    }

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
