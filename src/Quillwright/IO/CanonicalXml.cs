using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Quillwright.IO;

/// <summary>
/// Canonical XML, in both the forms an OPC signature uses: the inclusive form of
/// <see href="https://www.w3.org/TR/2001/REC-xml-c14n-20010315">Canonical XML 1.0</see> and the
/// exclusive form of <see href="https://www.w3.org/TR/xml-exc-c14n/">Exclusive XML
/// Canonicalization 1.0</see>.
/// </summary>
/// <remarks>
/// <para>
/// Verifying a signature means hashing the bytes the signer hashed, and two XML documents that
/// say the same thing can be written a hundred ways. Canonicalisation is the rule that turns
/// either of them into the same bytes: UTF-8, no declaration, attributes in a defined order,
/// empty elements written as a pair of tags, and a defined set of namespace declarations
/// carried onto each element.
/// </para>
/// <para>
/// This is written out here rather than taken from <c>System.Security.Cryptography.Xml</c> for
/// one reason: that library is not guaranteed to survive trimming and this package is meant to.
/// Everything used here — LINQ to XML and the hashing primitives — is trim-safe, which is what
/// lets signature verification live in the core package rather than behind a boundary a caller
/// would have to cross.
/// </para>
/// </remarks>
internal static class CanonicalXml
{
    /// <summary>The URI of inclusive Canonical XML without comments.</summary>
    public const string Inclusive = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";

    /// <summary>The URI of inclusive Canonical XML with comments.</summary>
    public const string InclusiveWithComments = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315#WithComments";

    /// <summary>The URI of exclusive canonicalisation without comments.</summary>
    public const string Exclusive = "http://www.w3.org/2001/10/xml-exc-c14n#";

    /// <summary>The URI of exclusive canonicalisation with comments.</summary>
    public const string ExclusiveWithComments = "http://www.w3.org/2001/10/xml-exc-c14n#WithComments";

    /// <summary>The namespace the <c>xml</c> prefix is bound to without being declared.</summary>
    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";

    /// <summary>Whether an algorithm URI names a canonicalisation this can perform.</summary>
    /// <param name="algorithm">The URI the signature names.</param>
    public static bool Supports(string? algorithm) =>
        algorithm is Inclusive or InclusiveWithComments or Exclusive or ExclusiveWithComments;

    /// <summary>Parses markup the way canonicalisation needs it: every space kept.</summary>
    /// <param name="markup">The XML to parse.</param>
    public static XDocument Parse(string markup) =>
        XDocument.Parse(markup, LoadOptions.PreserveWhitespace);

    /// <summary>
    /// Parses XML bytes while preserving the encoding detection performed by an XML reader.
    /// </summary>
    /// <param name="markup">The XML bytes, including any declaration or byte-order mark.</param>
    public static XDocument Parse(byte[] markup)
    {
        ArgumentNullException.ThrowIfNull(markup);

        using var stream = new MemoryStream(markup, writable: false);
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false,
            IgnoreWhitespace = false,
            CloseInput = false,
        });

        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    /// <summary>Canonicalises an element and everything under it.</summary>
    /// <param name="element">The element, still attached to the document it came from.</param>
    /// <param name="algorithm">Which of the forms to use.</param>
    /// <returns>The canonical bytes, ready to be hashed.</returns>
    public static byte[] Canonicalize(XElement element, string algorithm)
    {
        ArgumentNullException.ThrowIfNull(element);

        bool exclusive = algorithm is Exclusive or ExclusiveWithComments;
        bool includeComments = algorithm is InclusiveWithComments or ExclusiveWithComments;
        var output = new StringBuilder();
        Write(output, element, exclusive, includeComments, [], apex: true);
        return Encoding.UTF8.GetBytes(output.ToString());
    }

    /// <summary>Writes one element and its descendants.</summary>
    /// <param name="output">Where the canonical text goes.</param>
    /// <param name="element">The element to write.</param>
    /// <param name="exclusive">Whether to carry only the namespaces the element itself uses.</param>
    /// <param name="includeComments">Whether comments are part of the canonical output.</param>
    /// <param name="rendered">
    /// The declarations already written by an ancestor <em>of the output</em>, which is not the
    /// same as the ones in scope in the source: the element at the top of the subset re-declares
    /// everything it needs, because its ancestors are not being written at all.
    /// </param>
    /// <param name="apex">Whether this is the outermost element being written.</param>
    private static void Write(
        StringBuilder output, XElement element, bool exclusive, bool includeComments,
        Dictionary<string, string> rendered, bool apex = false)
    {
        var inherited = new Dictionary<string, string>(rendered, StringComparer.Ordinal);
        List<(string Prefix, string Uri)> namespaces = Declarations(element, exclusive, inherited);
        List<XAttribute> attributes = [.. element.Attributes().Where(static a => !a.IsNamespaceDeclaration)];

        if (apex && !exclusive)
            attributes.AddRange(InheritedXmlAttributes(element));

        attributes.Sort(CompareAttributes);

        string name = Qualified(element, element.Name);
        output.Append('<').Append(name);

        foreach ((string prefix, string uri) in namespaces)
            Attribute(output, prefix.Length == 0 ? "xmlns" : "xmlns:" + prefix, uri);

        foreach (XAttribute attribute in attributes)
            Attribute(output, Qualified(element, attribute.Name), attribute.Value);

        output.Append('>');
        foreach (XNode node in element.Nodes())
            Node(output, node, exclusive, includeComments, inherited);

        output.Append("</").Append(name).Append('>');
    }

    private static void Node(
        StringBuilder output, XNode node, bool exclusive, bool includeComments,
        Dictionary<string, string> inherited)
    {
        switch (node)
        {
            case XElement child:
                Write(output, child, exclusive, includeComments, inherited);
                return;

            case XText text:
                Text(output, text.Value);
                return;

            case XProcessingInstruction instruction:
                output.Append("<?").Append(instruction.Target);
                if (instruction.Data.Length > 0)
                    output.Append(' ').Append(instruction.Data);

                output.Append("?>");
                return;

            case XComment comment when includeComments:
                output.Append("<!--").Append(comment.Value).Append("-->");
                return;
        }
    }

    /// <summary>
    /// The namespace declarations this element has to write, in the order they go, updating the
    /// map its children inherit.
    /// </summary>
    /// <remarks>
    /// The two forms differ here and nowhere else. Inclusive canonicalisation carries every
    /// declaration in scope down onto the outermost element of the subset and writes one
    /// wherever it changes; exclusive carries only the prefixes the element and its attribute
    /// names actually use, which is what lets a signed fragment survive being moved into a
    /// document that declares other prefixes.
    /// </remarks>
    private static List<(string Prefix, string Uri)> Declarations(
        XElement element, bool exclusive, Dictionary<string, string> inherited)
    {
        var declarations = new List<(string Prefix, string Uri)>();
        foreach ((string prefix, string uri) in exclusive ? Utilised(element) : InScope(element))
        {
            if (inherited.TryGetValue(prefix, out string? already) && already == uri)
                continue;

            // A default namespace of nothing is written only to undo one already rendered.
            if (uri.Length == 0 && !inherited.ContainsKey(prefix))
                continue;

            inherited[prefix] = uri;
            declarations.Add((prefix, uri));
        }

        declarations.Sort(static (left, right) => string.CompareOrdinal(left.Prefix, right.Prefix));
        return declarations;
    }

    /// <summary>
    /// The <c>xml:*</c> attributes an ancestor outside the subset put in scope, which the
    /// inclusive form carries onto the outermost element written and the exclusive form does
    /// not. An attribute the element states itself wins.
    /// </summary>
    private static IEnumerable<XAttribute> InheritedXmlAttributes(XElement element)
    {
        var found = new Dictionary<string, XAttribute>(StringComparer.Ordinal);
        for (XElement? current = element.Parent; current is not null; current = current.Parent)
        {
            foreach (XAttribute attribute in current.Attributes())
            {
                if (attribute.Name.NamespaceName == XmlNamespace)
                    found.TryAdd(attribute.Name.LocalName, attribute);
            }
        }

        foreach (XAttribute own in element.Attributes())
        {
            if (own.Name.NamespaceName == XmlNamespace)
                found.Remove(own.Name.LocalName);
        }

        return found.Values;
    }

    /// <summary>Every prefix in scope at an element, nearest declaration winning.</summary>
    private static IEnumerable<(string Prefix, string Uri)> InScope(XElement element)
    {
        var scope = new SortedDictionary<string, string>(StringComparer.Ordinal);
        for (XElement? current = element; current is not null; current = current.Parent)
        {
            foreach (XAttribute declaration in current.Attributes())
            {
                if (!declaration.IsNamespaceDeclaration)
                    continue;

                string prefix = declaration.Name.LocalName == "xmlns" ? string.Empty : declaration.Name.LocalName;
                if (prefix != "xml")
                    scope.TryAdd(prefix, declaration.Value);
            }
        }

        return scope.Select(static entry => (entry.Key, entry.Value));
    }

    /// <summary>
    /// The prefixes an element's own name and attribute names use, which is what exclusive
    /// canonicalisation calls visibly utilised. The <c>xml</c> prefix is bound without being
    /// declared, so it is never written.
    /// </summary>
    private static IEnumerable<(string Prefix, string Uri)> Utilised(XElement element)
    {
        var used = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [Prefix(element, element.Name)] = element.Name.NamespaceName,
        };

        foreach (XAttribute attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name.NamespaceName.Length == 0 ||
                attribute.Name.NamespaceName == XmlNamespace)
            {
                continue;
            }

            used[Prefix(element, attribute.Name)] = attribute.Name.NamespaceName;
        }

        return used.Select(static entry => (entry.Key, entry.Value));
    }

    /// <summary>
    /// Attributes are ordered by namespace and then by local name, which puts every unqualified
    /// attribute before every qualified one.
    /// </summary>
    private static int CompareAttributes(XAttribute left, XAttribute right)
    {
        int order = string.CompareOrdinal(left.Name.NamespaceName, right.Name.NamespaceName);
        return order != 0 ? order : string.CompareOrdinal(left.Name.LocalName, right.Name.LocalName);
    }

    /// <summary>A name as it is written, with the prefix the document bound to its namespace.</summary>
    private static string Qualified(XElement owner, XName name)
    {
        string prefix = Prefix(owner, name);
        return prefix.Length == 0 ? name.LocalName : prefix + ":" + name.LocalName;
    }

    private static string Prefix(XElement owner, XName name) => name.NamespaceName.Length == 0
        ? string.Empty
        : name.NamespaceName == XmlNamespace ? "xml" : owner.GetPrefixOfNamespace(name.Namespace) ?? string.Empty;

    private static void Attribute(StringBuilder output, string name, string value)
    {
        output.Append(' ').Append(name).Append("=\"");
        foreach (char c in value)
        {
            _ = c switch
            {
                '&' => output.Append("&amp;"),
                '<' => output.Append("&lt;"),
                '"' => output.Append("&quot;"),
                '\u0009' => output.Append("&#x9;"),
                '\u000A' => output.Append("&#xA;"),
                '\u000D' => output.Append("&#xD;"),
                _ => output.Append(c),
            };
        }

        output.Append('"');
    }

    private static void Text(StringBuilder output, string value)
    {
        foreach (char c in value)
        {
            _ = c switch
            {
                '&' => output.Append("&amp;"),
                '<' => output.Append("&lt;"),
                '>' => output.Append("&gt;"),
                '\u000D' => output.Append("&#xD;"),
                _ => output.Append(c),
            };
        }
    }
}
