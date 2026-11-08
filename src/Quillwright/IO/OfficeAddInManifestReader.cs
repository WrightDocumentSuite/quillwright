using System.Globalization;
using System.Text;
using System.Xml;
using Quillwright.Diagnostics;
using Quillwright.Model;

namespace Quillwright.IO;

/// <summary>Reads the manifest of an Office add-in ([MS-OWEMXML]).</summary>
/// <remarks>
/// <para>
/// The manifest is not stored in a document (1.5): it is a file of its own, distributed through
/// an add-in catalogue. Nothing here fetches a manifest, resolves an entity, or follows any of
/// the addresses it reads.
/// </para>
/// <para>
/// The three add-in kinds and four <c>VersionOverrides</c> vocabularies extend the shared base.
/// This reader models the shared metadata and returns override subtrees as standalone markup.
/// </para>
/// </remarks>
public static class OfficeAddInManifestReader
{
    private const string Namespace10 = "http://schemas.microsoft.com/office/appforoffice/1.0";
    private const string Namespace11 = "http://schemas.microsoft.com/office/appforoffice/1.1";
    private const string NamespaceSchemaInstance = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>
    /// Reads a manifest with the shared default load budget, or returns <see langword="null"/>
    /// when the bytes are not XML, have the wrong root, or use an unknown base namespace.
    /// </summary>
    /// <param name="bytes">The whole manifest file.</param>
    /// <remarks>
    /// DTDs and external entities are prohibited. Resource-limit failures are reported as
    /// <see cref="DocumentLoadLimitException"/> rather than malformed XML.
    /// </remarks>
    public static OfficeAddInManifest? Read(byte[] bytes) =>
        Read(bytes, DocumentLoadBudget.Default, CancellationToken.None);

    /// <summary>
    /// Reads a manifest under a caller-supplied resource budget, or returns
    /// <see langword="null"/> when its XML or root element is invalid.
    /// </summary>
    /// <param name="bytes">The whole manifest file.</param>
    /// <param name="budget">Limits for input bytes, decoded XML, nodes and nesting depth.</param>
    /// <param name="cancellationToken">Stops parsing and result materialization.</param>
    /// <remarks>
    /// Limits are inclusive. A limit breach throws <see cref="DocumentLoadLimitException"/>;
    /// cancellation throws <see cref="OperationCanceledException"/>. DTDs and external entities
    /// remain prohibited, and malformed XML still produces <see langword="null"/>.
    /// </remarks>
    public static OfficeAddInManifest? Read(
        byte[] bytes,
        DocumentLoadBudget budget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(budget);

        var loadBudget = new DocumentLoadBudgetState(budget);
        cancellationToken.ThrowIfCancellationRequested();
        DocumentLoadBudgetState.Ensure(
            nameof(DocumentLoadBudget.MaxInputBytes), budget.MaxInputBytes, bytes.LongLength);

        try
        {
            using var xml = XmlReader.Create(
                new MemoryStream(bytes, writable: false),
                Xml.XmlDefaults.ForBudget(budget));
            return Read(xml, loadBudget, cancellationToken);
        }
        catch (XmlException exception) when (IsXmlCharacterLimit(exception))
        {
            long limit = budget.MaxXmlCharactersPerPart;
            throw new DocumentLoadLimitException(
                nameof(DocumentLoadBudget.MaxXmlCharactersPerPart),
                limit,
                limit == long.MaxValue ? long.MaxValue : limit + 1);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static OfficeAddInManifest? Read(
        XmlReader xml,
        DocumentLoadBudgetState loadBudget,
        CancellationToken cancellationToken)
    {
        Draft? draft = null;
        Capture? capture = null;
        bool rootClosed = false;
        var containers = new List<Container>();

        try
        {
            while (ReadNode(xml, loadBudget, cancellationToken))
            {
                if (draft is null)
                {
                    if (xml.NodeType != XmlNodeType.Element)
                        continue;

                    if (xml.LocalName != "OfficeApp" || xml.NamespaceURI is not (Namespace10 or Namespace11))
                        return null;

                    draft = new Draft(
                        xml.NamespaceURI,
                        xml.GetAttribute("type", NamespaceSchemaInstance),
                        loadBudget,
                        cancellationToken);
                    if (xml.IsEmptyElement)
                    {
                        rootClosed = true;
                        continue;
                    }

                    containers.Add(new Container(xml.Depth, string.Empty));
                    continue;
                }

                if (rootClosed)
                {
                    if (xml.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                        continue;
                    return null;
                }

                if (capture is not null)
                {
                    capture.Take(xml);
                    if (xml.NodeType == XmlNodeType.EndElement && xml.Depth == capture.Depth)
                    {
                        capture.Complete();
                        capture.Dispose();
                        capture = null;
                    }

                    continue;
                }

                if (xml.NodeType == XmlNodeType.Element)
                {
                    capture = StartElement(xml, draft, containers, loadBudget, cancellationToken);
                    if (capture is not null && xml.IsEmptyElement)
                    {
                        capture.Complete();
                        capture.Dispose();
                        capture = null;
                    }

                    continue;
                }

                if (xml.NodeType == XmlNodeType.EndElement &&
                    containers.Count > 0 && containers[^1].Depth == xml.Depth)
                {
                    Container complete = containers[^1];
                    containers.RemoveAt(containers.Count - 1);
                    draft.Add(complete.Surface);
                    if (containers.Count == 0)
                        rootClosed = true;
                }
            }

            return rootClosed ? draft?.Build() : null;
        }
        finally
        {
            capture?.Dispose();
        }
    }

    private static Capture? StartElement(
        XmlReader xml,
        Draft draft,
        List<Container> containers,
        DocumentLoadBudgetState loadBudget,
        CancellationToken cancellationToken)
    {
        if (containers.Count == 0)
            return new IgnoreCapture(xml.Depth);

        if (xml.LocalName == "VersionOverrides")
            return new VersionOverridesCapture(xml, draft, loadBudget, cancellationToken);

        if (xml.NamespaceURI != draft.Namespace)
            return new IgnoreCapture(xml.Depth);

        Container parent = containers[^1];
        switch (xml.LocalName)
        {
            case "SourceLocation":
                return new LocaleCapture(
                    xml, draft.Materialization, value => parent.Surface.Url = value);
            case "RequestedWidth":
                return new TextCapture(xml.Depth, loadBudget, cancellationToken,
                    value => parent.Surface.Width = Number(value));
            case "RequestedHeight":
                return new TextCapture(xml.Depth, loadBudget, cancellationToken,
                    value => parent.Surface.Height = Number(value));
            case "Hosts":
                return new NamesCapture(xml.Depth, "Host", draft.Hosts, draft.Materialization);
            case "Capabilities":
                return new NamesCapture(xml.Depth, "Capability", draft.Capabilities, draft.Materialization);
        }

        if (parent.Path.Length == 0 && Identity(xml, draft, loadBudget, cancellationToken) is { } identity)
            return identity;

        var child = new Container(xml.Depth, draft.Join(parent.Path, xml));
        if (xml.IsEmptyElement)
            draft.Add(child.Surface);
        else
            containers.Add(child);

        return null;
    }

    private static Capture? Identity(
        XmlReader xml,
        Draft draft,
        DocumentLoadBudgetState loadBudget,
        CancellationToken cancellationToken) => xml.LocalName switch
        {
            "Id" => new TextCapture(xml.Depth, loadBudget, cancellationToken, draft.SetId),
            "Version" => new TextCapture(xml.Depth, loadBudget, cancellationToken, draft.SetVersion),
            "ProviderName" => new TextCapture(xml.Depth, loadBudget, cancellationToken, draft.SetProviderName),
            "DefaultLocale" => new TextCapture(xml.Depth, loadBudget, cancellationToken, draft.SetDefaultLocale),
            "Permissions" => new TextCapture(xml.Depth, loadBudget, cancellationToken, draft.SetPermissions),
            "DisplayName" => new LocaleCapture(xml, draft.Materialization, draft.SetDisplayName),
            "Description" => new LocaleCapture(xml, draft.Materialization, draft.SetDescription),
            _ => null,
        };

    private static bool ReadNode(
        XmlReader xml,
        DocumentLoadBudgetState loadBudget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool read = xml.Read();
        cancellationToken.ThrowIfCancellationRequested();
        if (read)
            loadBudget.AddXmlNode(xml.Depth + 1);
        return read;
    }

    private static bool IsXmlCharacterLimit(XmlException exception) =>
        exception.Message.Contains(nameof(XmlReaderSettings.MaxCharactersInDocument), StringComparison.Ordinal);

    private static int? Number(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;

    private static string Local(string qualified)
    {
        int colon = qualified.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? qualified : qualified[(colon + 1)..];
    }

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

    private sealed class Container(int depth, string path)
    {
        public int Depth { get; } = depth;
        public string Path { get; } = path;
        public Surface Surface { get; } = new(path);
    }

    private abstract class Capture(int depth) : IDisposable
    {
        public int Depth { get; } = depth;
        public virtual void Take(XmlReader xml) { }
        public virtual void Complete() { }
        public virtual void Dispose() { }
    }

    private sealed class IgnoreCapture(int depth) : Capture(depth);

    private sealed class TextCapture : Capture
    {
        private readonly BoundedText _text;
        private readonly Action<string?> _complete;

        public TextCapture(
            int depth,
            DocumentLoadBudgetState loadBudget,
            CancellationToken cancellationToken,
            Action<string?> complete)
            : base(depth)
        {
            _text = new BoundedText(loadBudget, cancellationToken);
            _complete = complete;
        }

        public override void Take(XmlReader xml)
        {
            if (xml.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace)
                _text.Append(xml.Value);
        }

        public override void Complete()
        {
            string content = _text.ToString().Trim();
            _complete(content.Length == 0 ? null : content);
        }
    }

    private sealed class LocaleCapture : Capture
    {
        private readonly MaterializationBudget _materialization;
        private readonly Action<LocaleAwareValue> _complete;
        private readonly List<LocaleOverride> _overrides = [];
        private readonly string? _defaultValue;

        public LocaleCapture(
            XmlReader xml,
            MaterializationBudget materialization,
            Action<LocaleAwareValue> complete)
            : base(xml.Depth)
        {
            _materialization = materialization;
            _complete = complete;
            _defaultValue = _materialization.Retain(xml.GetAttribute("DefaultValue"));
        }

        public override void Take(XmlReader xml)
        {
            if (xml.NodeType != XmlNodeType.Element || xml.Depth != Depth + 1 || xml.LocalName != "Override")
                return;

            if (xml.GetAttribute("Locale") is { } locale && xml.GetAttribute("Value") is { } wording)
            {
                _overrides.Add(new LocaleOverride(
                    _materialization.Retain(locale)!,
                    _materialization.Retain(wording)!));
            }
        }

        public override void Complete() => _complete(new LocaleAwareValue
        {
            DefaultValue = _defaultValue,
            Overrides = _overrides,
        });
    }

    private sealed class NamesCapture : Capture
    {
        private readonly string _localName;
        private readonly List<string> _names;
        private readonly MaterializationBudget _materialization;

        public NamesCapture(
            int depth,
            string localName,
            List<string> names,
            MaterializationBudget materialization)
            : base(depth)
        {
            _localName = localName;
            _names = names;
            _materialization = materialization;
        }

        public override void Take(XmlReader xml)
        {
            if (xml.NodeType == XmlNodeType.Element && xml.Depth == Depth + 1 &&
                xml.LocalName == _localName && xml.GetAttribute("Name") is { } name)
            {
                _names.Add(_materialization.Retain(name)!);
            }
        }
    }

    private sealed class VersionOverridesCapture : Capture
    {
        private readonly Draft _draft;
        private readonly string _namespace;
        private readonly BoundedTextWriter _text;
        private readonly XmlWriter _writer;
        private readonly CancellationToken _cancellationToken;

        public VersionOverridesCapture(
            XmlReader xml,
            Draft draft,
            DocumentLoadBudgetState loadBudget,
            CancellationToken cancellationToken)
            : base(xml.Depth)
        {
            _draft = draft;
            _namespace = xml.NamespaceURI;
            _cancellationToken = cancellationToken;
            _text = new BoundedTextWriter(loadBudget, cancellationToken);
            _writer = XmlWriter.Create(_text, new XmlWriterSettings
            {
                ConformanceLevel = ConformanceLevel.Fragment,
                OmitXmlDeclaration = true,
                NamespaceHandling = NamespaceHandling.OmitDuplicates,
            });
            WriteElement(xml, includeInScopeNamespaces: true);
        }

        public override void Take(XmlReader xml)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            switch (xml.NodeType)
            {
                case XmlNodeType.Element:
                    WriteElement(xml, includeInScopeNamespaces: false);
                    break;
                case XmlNodeType.EndElement:
                    _writer.WriteFullEndElement();
                    break;
                case XmlNodeType.Text:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    _writer.WriteString(xml.Value);
                    break;
                case XmlNodeType.CDATA:
                    _writer.WriteCData(xml.Value);
                    break;
                case XmlNodeType.EntityReference:
                    _writer.WriteEntityRef(xml.Name);
                    break;
            }
        }

        public override void Complete()
        {
            _writer.Flush();
            _draft.AddVersionOverrides(_namespace, _text.ToString());
        }

        public override void Dispose() => _writer.Dispose();

        private void WriteElement(XmlReader xml, bool includeInScopeNamespaces)
        {
            _writer.WriteStartElement(xml.Prefix, xml.LocalName, xml.NamespaceURI);
            if (includeInScopeNamespaces && xml is IXmlNamespaceResolver resolver)
            {
                foreach ((string prefix, string uri) in resolver.GetNamespacesInScope(XmlNamespaceScope.All))
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    if (prefix is not ("xml" or "xmlns"))
                        WriteNamespace(prefix, uri);
                }
            }

            if (xml.MoveToFirstAttribute())
            {
                do
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    if (!includeInScopeNamespaces || !IsNamespaceDeclaration(xml))
                        WriteAttribute(xml);
                }
                while (xml.MoveToNextAttribute());
                xml.MoveToElement();
            }

            if (xml.IsEmptyElement)
                _writer.WriteEndElement();
        }

        private void WriteNamespace(string prefix, string uri)
        {
            if (prefix.Length == 0)
                _writer.WriteAttributeString("xmlns", uri);
            else
                _writer.WriteAttributeString("xmlns", prefix, null, uri);
        }

        private void WriteAttribute(XmlReader xml)
        {
            if (IsNamespaceDeclaration(xml))
                WriteNamespace(xml.Prefix.Length == 0 ? string.Empty : xml.LocalName, xml.Value);
            else
                _writer.WriteAttributeString(xml.Prefix, xml.LocalName, xml.NamespaceURI, xml.Value);
        }

        private static bool IsNamespaceDeclaration(XmlReader xml) =>
            xml.Prefix == "xmlns" || (xml.Prefix.Length == 0 && xml.LocalName == "xmlns");
    }

    private sealed class BoundedText
    {
        private readonly StringBuilder _builder = new();
        private readonly DocumentLoadBudgetState _loadBudget;
        private readonly CancellationToken _cancellationToken;

        public BoundedText(DocumentLoadBudgetState loadBudget, CancellationToken cancellationToken)
        {
            _loadBudget = loadBudget;
            _cancellationToken = cancellationToken;
        }

        public void Append(string value)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            long observed = (long)_builder.Length + value.Length;
            DocumentLoadBudgetState.Ensure(
                nameof(DocumentLoadBudget.MaxXmlCharactersPerPart),
                _loadBudget.Budget.MaxXmlCharactersPerPart,
                observed);
            _builder.Append(value);
            _cancellationToken.ThrowIfCancellationRequested();
        }

        public override string ToString() => _builder.ToString();
    }

    private sealed class BoundedTextWriter : TextWriter
    {
        private readonly StringBuilder _builder = new();
        private readonly DocumentLoadBudgetState _loadBudget;
        private readonly CancellationToken _cancellationToken;

        public BoundedTextWriter(DocumentLoadBudgetState loadBudget, CancellationToken cancellationToken)
        {
            _loadBudget = loadBudget;
            _cancellationToken = cancellationToken;
        }

        public override Encoding Encoding => Encoding.Unicode;

        public override void Write(char value)
        {
            Charge(1);
            _builder.Append(value);
            _cancellationToken.ThrowIfCancellationRequested();
        }

        public override void Write(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            Charge(count);
            _builder.Append(buffer, index, count);
            _cancellationToken.ThrowIfCancellationRequested();
        }

        public override void Write(ReadOnlySpan<char> buffer)
        {
            Charge(buffer.Length);
            _builder.Append(buffer);
            _cancellationToken.ThrowIfCancellationRequested();
        }

        public override void Write(string? value)
        {
            if (value is null)
                return;
            Charge(value.Length);
            _builder.Append(value);
            _cancellationToken.ThrowIfCancellationRequested();
        }

        public override string ToString() => _builder.ToString();

        private void Charge(int characters)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            long observed = (long)_builder.Length + characters;
            DocumentLoadBudgetState.Ensure(
                nameof(DocumentLoadBudget.MaxXmlCharactersPerPart),
                _loadBudget.Budget.MaxXmlCharactersPerPart,
                observed);
        }
    }

    private sealed class MaterializationBudget
    {
        private readonly DocumentLoadBudgetState _loadBudget;
        private readonly CancellationToken _cancellationToken;
        private long _characters;

        public MaterializationBudget(DocumentLoadBudgetState loadBudget, CancellationToken cancellationToken)
        {
            _loadBudget = loadBudget;
            _cancellationToken = cancellationToken;
        }

        public string? Retain(string? value)
        {
            if (value is null)
                return null;

            _cancellationToken.ThrowIfCancellationRequested();
            long observed = value.Length > long.MaxValue - _characters
                ? long.MaxValue
                : _characters + value.Length;
            DocumentLoadBudgetState.Ensure(
                nameof(DocumentLoadBudget.MaxXmlCharactersPerPart),
                _loadBudget.Budget.MaxXmlCharactersPerPart,
                observed);
            _characters = observed;
            _cancellationToken.ThrowIfCancellationRequested();
            return value;
        }
    }

    private sealed class Draft
    {
        private readonly string? _declaredType;
        private readonly MaterializationBudget _materialization;

        public Draft(
            string @namespace,
            string? declaredType,
            DocumentLoadBudgetState loadBudget,
            CancellationToken cancellationToken)
        {
            _materialization = new MaterializationBudget(loadBudget, cancellationToken);
            Namespace = _materialization.Retain(@namespace)!;
            _declaredType = _materialization.Retain(declaredType);
        }

        public string Namespace { get; }
        public MaterializationBudget Materialization => _materialization;
        public string? Id { get; private set; }
        public string? Version { get; private set; }
        public string? ProviderName { get; private set; }
        public string? DefaultLocale { get; private set; }
        public string? Permissions { get; private set; }
        public LocaleAwareValue? DisplayName { get; private set; }
        public LocaleAwareValue? Description { get; private set; }
        public List<string> Hosts { get; } = [];
        public List<string> Capabilities { get; } = [];
        public List<OfficeAddInSourceLocation> SourceLocations { get; } = [];
        public List<OfficeAddInVersionOverrides> VersionOverrides { get; } = [];

        public void SetId(string? value) => Id ??= _materialization.Retain(value);
        public void SetVersion(string? value) => Version ??= _materialization.Retain(value);
        public void SetProviderName(string? value) => ProviderName ??= _materialization.Retain(value);
        public void SetDefaultLocale(string? value) => DefaultLocale ??= _materialization.Retain(value);
        public void SetPermissions(string? value) => Permissions ??= _materialization.Retain(value);
        public void SetDisplayName(LocaleAwareValue value) => DisplayName ??= value;
        public void SetDescription(LocaleAwareValue value) => Description ??= value;

        public string Join(string path, XmlReader xml)
        {
            string name = xml.GetAttribute("type", NamespaceSchemaInstance) is { Length: > 0 } declared
                ? $"{xml.LocalName}[{Local(declared)}]"
                : xml.LocalName;
            string joined = path.Length == 0 ? name : $"{path}/{name}";
            return _materialization.Retain(joined)!;
        }

        public void Add(Surface surface)
        {
            if (surface.Build() is { } location)
                SourceLocations.Add(location);
        }

        public void AddVersionOverrides(string @namespace, string markup) =>
            VersionOverrides.Add(new OfficeAddInVersionOverrides
            {
                Namespace = _materialization.Retain(@namespace)!,
                Markup = _materialization.Retain(markup)!,
            });

        public OfficeAddInManifest Build() => new()
        {
            Namespace = Namespace,
            Kind = Kind(_declaredType),
            DeclaredType = _declaredType,
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
