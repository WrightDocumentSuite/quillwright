using System.Globalization;
using System.Text;
using System.Xml;
using Quillwright.Model;

namespace Quillwright.IO;

/// <summary>
/// Reads the manifest of an Office add-in ([MS-OWEMXML]).
/// </summary>
/// <remarks>
/// <para>
/// The manifest is not stored in a document (1.5): it is a file of its own, distributed through
/// an add-in catalogue, so this is a standalone reader in the same way the co-authoring lock
/// reader is — hand it the bytes and it says what the add-in claims to be. Nothing here fetches
/// a manifest, resolves an entity, or follows any of the addresses it reads.
/// </para>
/// <para>
/// What comes back is the metadata the two base namespaces share, and no more. The three add-in
/// kinds each extend that base with a vocabulary of their own, and four further
/// <c>VersionOverrides</c> vocabularies sit on top; those are not modelled, and the override
/// subtrees are returned as markup so that a caller who understands one is not left without it.
/// </para>
/// </remarks>
public static class OfficeAddInManifestReader
{
    /// <summary>The first base namespace ([MS-OWEMXML] 2.1.1).</summary>
    private const string Namespace10 = "http://schemas.microsoft.com/office/appforoffice/1.0";

    /// <summary>The second base namespace ([MS-OWEMXML] 2.1.2).</summary>
    private const string Namespace11 = "http://schemas.microsoft.com/office/appforoffice/1.1";

    private const string NamespaceSchemaInstance = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>
    /// Reads a manifest, or returns <see langword="null"/> when the bytes are not XML, are not
    /// rooted in an <c>OfficeApp</c> element, or use a base namespace this does not know.
    /// </summary>
    /// <param name="bytes">The whole manifest file.</param>
    public static OfficeAddInManifest? Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            using var xml = XmlReader.Create(new MemoryStream(bytes), Xml.XmlDefaults.ReaderSettings);
            if (xml.MoveToContent() != XmlNodeType.Element || xml.LocalName != "OfficeApp" ||
                xml.NamespaceURI is not (Namespace10 or Namespace11))
            {
                return null;
            }

            var draft = new Draft(xml.NamespaceURI, xml.GetAttribute("type", NamespaceSchemaInstance));
            ReadChildren(xml, draft, path: string.Empty);
            return draft.Build();
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the direct children of the element the reader is on, filing the ones the base
    /// manifest defines and descending through the rest in search of source locations.
    /// </summary>
    /// <param name="xml">Reader positioned on the element.</param>
    /// <param name="draft">The manifest being assembled.</param>
    /// <param name="path">Where this element sits below the root; empty for the root itself.</param>
    private static void ReadChildren(XmlReader xml, Draft draft, string path)
    {
        if (xml.IsEmptyElement)
            return;

        var surface = new Surface(path);
        int depth = xml.Depth;
        while (xml.Read())
        {
            if (xml.NodeType == XmlNodeType.EndElement && xml.Depth == depth)
                break;

            if (xml.NodeType == XmlNodeType.Element && xml.Depth == depth + 1)
                Take(xml, draft, path, surface);
        }

        draft.Add(surface);
    }

    private static void Take(XmlReader xml, Draft draft, string path, Surface surface)
    {
        // An override subtree is in a namespace of its own and is kept whole, not read.
        if (xml.LocalName == "VersionOverrides")
        {
            draft.VersionOverrides.Add(new OfficeAddInVersionOverrides
            {
                Namespace = xml.NamespaceURI,
                Markup = Markup(xml),
            });
            return;
        }

        if (xml.NamespaceURI != draft.Namespace)
            return;

        switch (xml.LocalName)
        {
            case "SourceLocation": surface.Url = LocaleAware(xml); break;
            case "RequestedWidth": surface.Width = Number(Text(xml)); break;
            case "RequestedHeight": surface.Height = Number(Text(xml)); break;
            case "Hosts": Names(xml, "Host", draft.Hosts); break;
            case "Capabilities": Names(xml, "Capability", draft.Capabilities); break;
            default:
                if (path.Length == 0)
                    TakeIdentity(xml, draft);
                else
                    ReadChildren(xml, draft, Join(path, xml));
                break;
        }
    }

    /// <summary>Reads the elements every kind of add-in shares (2.2.21).</summary>
    private static void TakeIdentity(XmlReader xml, Draft draft)
    {
        switch (xml.LocalName)
        {
            case "Id": draft.Id ??= Text(xml); break;
            case "Version": draft.Version ??= Text(xml); break;
            case "ProviderName": draft.ProviderName ??= Text(xml); break;
            case "DefaultLocale": draft.DefaultLocale ??= Text(xml); break;
            case "Permissions": draft.Permissions ??= Text(xml); break;
            case "DisplayName": draft.DisplayName ??= LocaleAware(xml); break;
            case "Description": draft.Description ??= LocaleAware(xml); break;
            default: ReadChildren(xml, draft, Join(string.Empty, xml)); break;
        }
    }

    /// <summary>Reads a <c>LocaleAwareSetting</c> (2.2.5): a default value and its translations.</summary>
    private static LocaleAwareValue LocaleAware(XmlReader xml)
    {
        string? value = xml.GetAttribute("DefaultValue");
        var overrides = new List<LocaleOverride>();

        foreach (XmlReader child in Elements(xml))
        {
            if (child.LocalName == "Override" &&
                child.GetAttribute("Locale") is { } locale && child.GetAttribute("Value") is { } wording)
            {
                overrides.Add(new LocaleOverride(locale, wording));
            }
        }

        return new LocaleAwareValue { DefaultValue = value, Overrides = overrides };
    }

    /// <summary>Collects the <c>Name</c> attribute of each child of one local name.</summary>
    private static void Names(XmlReader xml, string localName, List<string> into)
    {
        foreach (XmlReader child in Elements(xml))
        {
            if (child.LocalName == localName && child.GetAttribute("Name") is { } name)
                into.Add(name);
        }
    }

    /// <summary>Walks the direct element children of the element the reader is on.</summary>
    private static IEnumerable<XmlReader> Elements(XmlReader xml)
    {
        if (xml.IsEmptyElement)
            yield break;

        int depth = xml.Depth;
        while (xml.Read())
        {
            if (xml.NodeType == XmlNodeType.EndElement && xml.Depth == depth)
                yield break;

            if (xml.NodeType == XmlNodeType.Element && xml.Depth == depth + 1)
                yield return xml;
        }
    }

    /// <summary>
    /// The path an element sits at, with an <c>xsi:type</c> in brackets so that two forms of a
    /// mail add-in are told apart rather than written over one another.
    /// </summary>
    private static string Join(string path, XmlReader xml)
    {
        string name = xml.GetAttribute("type", NamespaceSchemaInstance) is { Length: > 0 } declared
            ? $"{xml.LocalName}[{Local(declared)}]"
            : xml.LocalName;

        return path.Length == 0 ? name : $"{path}/{name}";
    }

    /// <summary>
    /// The text of an element. Read through a reader of its own, because reading content steps
    /// past the closing tag and would otherwise carry the outer walk past a sibling with it.
    /// </summary>
    private static string? Text(XmlReader xml)
    {
        if (xml.IsEmptyElement)
            return null;

        var text = new StringBuilder();
        using XmlReader inside = xml.ReadSubtree();
        inside.Read();
        while (inside.Read())
        {
            if (inside.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace)
                text.Append(inside.Value);
        }

        string content = text.ToString().Trim();
        return content.Length == 0 ? null : content;
    }

    /// <summary>One subtree as it was written, namespace declarations and all.</summary>
    private static string Markup(XmlReader xml)
    {
        using XmlReader inside = xml.ReadSubtree();
        inside.Read();
        return inside.ReadOuterXml();
    }

    /// <summary>A <c>QName</c> without whichever prefix the manifest happened to bind.</summary>
    private static string Local(string qualified)
    {
        int colon = qualified.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? qualified : qualified[(colon + 1)..];
    }

    private static int? Number(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;

    /// <summary>One settings element while it is being read: a page and the size asked for it.</summary>
    private sealed class Surface(string path)
    {
        public LocaleAwareValue? Url { get; set; }

        public int? Width { get; set; }

        public int? Height { get; set; }

        public OfficeAddInSourceLocation? Build() => Url is null
            ? null
            : new OfficeAddInSourceLocation
            {
                Context = path,
                Url = Url,
                RequestedWidth = Width,
                RequestedHeight = Height,
            };
    }

    /// <summary>The manifest while it is being read.</summary>
    private sealed class Draft(string @namespace, string? declaredType)
    {
        public string Namespace { get; } = @namespace;

        public string? Id { get; set; }

        public string? Version { get; set; }

        public string? ProviderName { get; set; }

        public string? DefaultLocale { get; set; }

        public string? Permissions { get; set; }

        public LocaleAwareValue? DisplayName { get; set; }

        public LocaleAwareValue? Description { get; set; }

        public List<string> Hosts { get; } = [];

        public List<string> Capabilities { get; } = [];

        public List<OfficeAddInSourceLocation> SourceLocations { get; } = [];

        public List<OfficeAddInVersionOverrides> VersionOverrides { get; } = [];

        public void Add(Surface surface)
        {
            if (surface.Build() is { } location)
                SourceLocations.Add(location);
        }

        public OfficeAddInManifest Build() => new()
        {
            Namespace = Namespace,
            Kind = Kind(declaredType),
            DeclaredType = declaredType,
            Id = Id,
            Version = Version,
            ProviderName = ProviderName,
            DefaultLocale = DefaultLocale,
            DisplayName = DisplayName,
            Description = Description,
            Hosts = Hosts,
            Capabilities = Capabilities,
            Permissions = Permissions,
            SourceLocations = SourceLocations,
            VersionOverrides = VersionOverrides,
        };

        private static OfficeAddInKind Kind(string? declaredType) => Local(declaredType ?? string.Empty) switch
        {
            "ContentApp" => OfficeAddInKind.ContentApp,
            "TaskPaneApp" => OfficeAddInKind.TaskPaneApp,
            "MailApp" => OfficeAddInKind.MailApp,
            _ => OfficeAddInKind.Unknown,
        };
    }
}
