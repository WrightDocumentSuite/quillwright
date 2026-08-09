using System.Text;

namespace Quillwright.Html;

/// <summary>The namespaces a parsed element can be in (WHATWG HTML §13.2.6.5).</summary>
internal enum HtmlNamespace : byte
{
    /// <summary>The HTML namespace, which is nearly everything.</summary>
    Html,

    /// <summary>The SVG namespace, entered through an <c>svg</c> start tag.</summary>
    Svg,

    /// <summary>The MathML namespace, entered through a <c>math</c> start tag.</summary>
    MathML,
}

/// <summary>A node of the parsed tree.</summary>
internal abstract class HtmlNode
{
    /// <summary>The 1-based source line the node began on.</summary>
    public int Line { get; init; }

    /// <summary>The element this node sits in, or <see langword="null"/> for the root.</summary>
    public HtmlElement? Parent { get; internal set; }
}

/// <summary>An element, its attributes ASCII-lower-cased by name, its children in order.</summary>
internal sealed class HtmlElement : HtmlNode
{
    private readonly List<HtmlAttribute> _attributes = [];
    private readonly Dictionary<string, string> _attributesByName;

    public HtmlElement(string name, HtmlNamespace space = HtmlNamespace.Html)
        : this(name, space, StringComparer.Ordinal)
    {
    }

    /// <summary>Test seam for counting attribute-name operations without wall-clock assertions.</summary>
    internal HtmlElement(
        string name,
        HtmlNamespace space,
        IEqualityComparer<string> attributeNameComparer)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(attributeNameComparer);
        Name = name;
        Namespace = space;
        _attributesByName = new Dictionary<string, string>(attributeNameComparer);
    }

    /// <summary>The tag name, with ASCII uppercase characters lower-cased.</summary>
    public string Name { get; }

    /// <summary>Which namespace the element is in.</summary>
    public HtmlNamespace Namespace { get; }

    /// <summary>The attributes, in source order, duplicates already dropped.</summary>
    public IReadOnlyList<HtmlAttribute> Attributes => _attributes;

    /// <summary>The children, in order.</summary>
    public List<HtmlNode> Children { get; } = [];

    /// <summary>Whether this is an HTML element with the given name.</summary>
    /// <param name="name">The name to compare, lower-case.</param>
    public bool Is(string name) =>
        Namespace == HtmlNamespace.Html && string.Equals(Name, name, StringComparison.Ordinal);

    /// <summary>Whether this is an HTML element with any of the given names.</summary>
    /// <param name="names">The names to compare, lower-case.</param>
    public bool IsAny(params ReadOnlySpan<string> names)
    {
        if (Namespace != HtmlNamespace.Html)
            return false;

        foreach (string name in names)
        {
            if (string.Equals(Name, name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>The value of an attribute, or <see langword="null"/> when it has none.</summary>
    /// <param name="name">The attribute name, lower-case.</param>
    public string? Attribute(string name) =>
        _attributesByName.GetValueOrDefault(name);

    /// <summary>Adds an attribute unless its exact name is already present.</summary>
    internal bool AddAttribute(HtmlAttribute attribute)
    {
        if (!_attributesByName.TryAdd(attribute.Name, attribute.Value))
            return false;

        _attributes.Add(attribute);
        return true;
    }

    /// <summary>Adds attributes in order, keeping the first value for every exact name.</summary>
    internal void AddAttributes(IEnumerable<HtmlAttribute> attributes)
    {
        foreach (HtmlAttribute attribute in attributes)
            AddAttribute(attribute);
    }

    /// <summary>Appends a node, detaching it from wherever it was.</summary>
    /// <param name="child">The node to append.</param>
    public void Append(HtmlNode child)
    {
        child.Parent?.Children.Remove(child);
        Children.Add(child);
        child.Parent = this;
    }

    /// <summary>Inserts a node at a position, detaching it from wherever it was.</summary>
    /// <param name="index">Where to insert it.</param>
    /// <param name="child">The node to insert.</param>
    public void Insert(int index, HtmlNode child)
    {
        if (child.Parent == this)
        {
            int at = Children.IndexOf(child);
            if (at >= 0 && at < index)
                index--;
        }

        child.Parent?.Children.Remove(child);
        Children.Insert(Math.Clamp(index, 0, Children.Count), child);
        child.Parent = this;
    }

    /// <summary>Whether two elements were created with the same name and attributes.</summary>
    /// <param name="other">The element to compare with.</param>
    /// <remarks>
    /// What the Noah's Ark clause of §13.2.4.3 compares: name, namespace and attributes,
    /// order not mattering.
    /// </remarks>
    public bool SameAs(HtmlElement other)
    {
        if (Namespace != other.Namespace || !string.Equals(Name, other.Name, StringComparison.Ordinal) ||
            Attributes.Count != other.Attributes.Count)
        {
            return false;
        }

        foreach (HtmlAttribute attribute in Attributes)
        {
            if (other.Attribute(attribute.Name) != attribute.Value)
                return false;
        }

        return true;
    }
}

/// <summary>A stretch of text, character references already expanded.</summary>
internal sealed class HtmlText : HtmlNode
{
    private readonly StringBuilder _value = new();

    internal HtmlText(string value) => _value.Append(value);

    /// <summary>The characters.</summary>
    public string Value => _value.ToString();

    /// <summary>Adds characters to the end, which is what a text node in the DOM does.</summary>
    /// <param name="text">The characters to add.</param>
    internal void Append(string text) => _value.Append(text);
}

/// <summary>A comment. The importer steps over these; the parser keeps them so the tree is the tree.</summary>
internal sealed class HtmlComment(string data) : HtmlNode
{
    /// <summary>The comment's data.</summary>
    public string Data { get; } = data;
}

/// <summary>A processing instruction retained in the parsed tree.</summary>
internal sealed class HtmlProcessingInstruction(string target, string data) : HtmlNode
{
    /// <summary>The processing instruction target, preserving source case.</summary>
    public string Target { get; } = target;

    /// <summary>The processing instruction data.</summary>
    public string Data { get; } = data;
}

/// <summary>The document type declaration retained as a child of the document node.</summary>
internal sealed class HtmlDocumentType(string name, string publicIdentifier, string systemIdentifier) : HtmlNode
{
    /// <summary>The doctype name.</summary>
    public string Name { get; } = name;

    /// <summary>The public identifier, or the empty string when it was missing.</summary>
    public string PublicIdentifier { get; } = publicIdentifier;

    /// <summary>The system identifier, or the empty string when it was missing.</summary>
    public string SystemIdentifier { get; } = systemIdentifier;
}
