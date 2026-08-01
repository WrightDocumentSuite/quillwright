using System.Globalization;
using System.Xml;
using Quillwright.IO;
using Quillwright.Model;

namespace Quillwright.Formats;

/// <summary>
/// Reads the web extension parts of a package ([MS-OWEXML]).
/// </summary>
/// <remarks>
/// <para>
/// The parts come in pairs: <c>taskpanes.xml</c> says where each add-in's pane sits, and one
/// <c>webextensionN.xml</c> per add-in says which add-in it is and what it saved. Both are
/// preserved verbatim; this only reads them, so that a caller can tell what a document will
/// try to load without parsing the parts itself.
/// </para>
/// <para>
/// [MS-OWEXML] is the in-document format and is the only one a package can supply. The add-in
/// manifest ([MS-OWEMXML]) is a different specification for a different file, which is not
/// persisted into a document at all.
/// </para>
/// </remarks>
internal static class WebExtensionReader
{
    private const string NsTaskPanes = "http://schemas.microsoft.com/office/webextensions/taskpanes/2010/11";
    private const string NsWebExtension = "http://schemas.microsoft.com/office/webextensions/webextension/2010/11";

    /// <summary>Reads every web extension the package carries, panes first.</summary>
    /// <param name="preserved">The parts and relationships read from the package.</param>
    public static List<WebExtension> Read(PreservedPackage preserved)
    {
        var extensions = new List<WebExtension>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (TaskPanesPart(preserved) is { } panes)
        {
            foreach ((TaskPaneSettings settings, string? id) in Panes(preserved.Parts[panes]))
            {
                if (Target(preserved, panes, id) is not { } part || !seen.Add(part))
                    continue;

                if (Extension(preserved, part, settings) is { } extension)
                    extensions.Add(extension);
            }
        }

        // An extension the pane list does not mention is still in the package and still loads.
        foreach (string path in preserved.Parts.Keys.Where(static path => path.Contains("webextension", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            if (seen.Add(path) && Extension(preserved, path, pane: null) is { } extension)
                extensions.Add(extension);
        }

        return extensions;
    }

    private static string? TaskPanesPart(PreservedPackage preserved)
    {
        OpcRelationship relationship = preserved.MainRelationships.FirstOrDefault(
            static r => r.Is(DocxSchema.RelTaskPanes));
        if (relationship.Target is null || relationship.IsExternal)
            return null;

        string path = OpcPath.Resolve(preserved.MainPartPath, relationship.Target);
        return preserved.Parts.ContainsKey(path) ? path : null;
    }

    /// <summary>The panes of <c>taskpanes.xml</c>, each with the extension it shows.</summary>
    private static List<(TaskPaneSettings Settings, string? RelationshipId)> Panes(byte[] content)
    {
        var panes = new List<(TaskPaneSettings, string?)>();
        TaskPaneSettings? current = null;

        try
        {
            using var xml = XmlReader.Create(new MemoryStream(content), Xml.XmlDefaults.ReaderSettings);
            while (xml.Read())
            {
                if (xml.NodeType != XmlNodeType.Element || xml.NamespaceURI != NsTaskPanes)
                    continue;

                switch (xml.LocalName)
                {
                    case "taskpane":
                        current = new TaskPaneSettings
                        {
                            DockState = xml.GetAttribute("dockstate"),
                            IsVisible = Flag(xml.GetAttribute("visibility")),
                            Width = Number(xml.GetAttribute("width")),
                            Row = Number(xml.GetAttribute("row")),
                        };
                        break;
                    case "webextensionref" when current is not null:
                        panes.Add((current, XmlHelp.RelAttr(xml)));
                        current = null;
                        break;
                }
            }
        }
        catch (XmlException)
        {
            return panes;
        }

        return panes;
    }

    private static string? Target(PreservedPackage preserved, string source, string? relationshipId)
    {
        if (relationshipId is null || !preserved.Relationships.TryGetValue(source, out List<OpcRelationship>? links))
            return null;

        OpcRelationship relationship = links.FirstOrDefault(r => r.Id == relationshipId);
        if (relationship.Target is null || relationship.IsExternal)
            return null;

        string path = OpcPath.Resolve(source, relationship.Target);
        return preserved.Parts.ContainsKey(path) ? path : null;
    }

    /// <summary>Reads one <c>webextensionN.xml</c>.</summary>
    private static WebExtension? Extension(PreservedPackage preserved, string path, TaskPaneSettings? pane)
    {
        string? id = null;
        string? storeId = null;
        string? version = null;
        string? store = null;
        string? storeType = null;
        var properties = new List<WebExtensionProperty>();
        bool found = false;

        try
        {
            using var xml = XmlReader.Create(new MemoryStream(preserved.Parts[path]), Xml.XmlDefaults.ReaderSettings);
            while (xml.Read())
            {
                if (xml.NodeType != XmlNodeType.Element || xml.NamespaceURI != NsWebExtension)
                    continue;

                switch (xml.LocalName)
                {
                    case "webextension":
                        found = true;
                        id = xml.GetAttribute("id");
                        break;
                    case "reference" when storeId is null:
                        storeId = xml.GetAttribute("id");
                        version = xml.GetAttribute("version");
                        store = xml.GetAttribute("store");
                        storeType = xml.GetAttribute("storeType");
                        break;
                    case "property" when xml.GetAttribute("name") is { } name:
                        properties.Add(new WebExtensionProperty(name, xml.GetAttribute("value")));
                        break;
                }
            }
        }
        catch (XmlException)
        {
            return null;
        }

        return found
            ? new WebExtension
            {
                PartPath = path,
                Id = id,
                StoreId = storeId,
                Version = version,
                Store = store,
                StoreType = storeType,
                Properties = properties,
                TaskPane = pane,
            }
            : null;
    }

    private static bool Flag(string? value) => value is not null && value is not ("0" or "false" or "off");

    private static int? Number(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
}
