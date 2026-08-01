using System.Xml.Linq;

namespace Quillwright.IO;

/// <summary>
/// The relationships transform of ECMA-376 part 2, clause 10.6: what a signature actually
/// hashes when it covers a <c>.rels</c> part.
/// </summary>
/// <remarks>
/// <para>
/// A signature that covered a relationships part as it stands would break the moment anything
/// added a relationship to it — a new image, a new header — even though nothing signed had
/// changed. So the transform builds a document of its own out of the part: only the
/// relationships the signature names, sorted by identifier, with the two optional attributes
/// filled in explicitly and everything else about the part left out. That document is what is
/// canonicalised and hashed.
/// </para>
/// </remarks>
internal static class RelationshipTransform
{
    /// <summary>The URI that names the transform.</summary>
    public const string Algorithm = "http://schemas.openxmlformats.org/package/2006/RelationshipTransform";

    private static readonly XNamespace Package = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Mce = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>
    /// Rebuilds a relationships part as the transform defines it.
    /// </summary>
    /// <param name="part">The <c>.rels</c> part, as bytes.</param>
    /// <param name="selection">
    /// What the transform selects: identifiers or relationship types.
    /// </param>
    /// <returns>The document to canonicalise, or <see langword="null"/> when the part will not parse.</returns>
    public static XDocument? Apply(byte[] part, RelationshipSelection selection)
    {
        ArgumentNullException.ThrowIfNull(part);

        // Clause 10.5.8.2 requires at least one RelationshipReference or
        // RelationshipsGroupReference inside the transform.
        if (selection.Ids.Count == 0 && selection.Types.Count == 0)
            return null;

        XDocument source;
        try
        {
            source = CanonicalXml.Parse(part);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        if (source.Root is not { } root || root.Name != Package + "Relationships" ||
            EffectiveChildren(root, MceContext.Empty) is not { } children)
        {
            return null;
        }

        var kept = new List<XElement>();
        foreach (XElement relationship in children.Where(static child => child.Name == Package + "Relationship"))
        {
            if (!selection.Covers(relationship.Attribute("Id")?.Value, relationship.Attribute("Type")?.Value))
                continue;

            kept.Add(Normalise(relationship));
        }

        // Sorted by identifier as a string of Unicode code points, which is what "sorted in
        // lexicographic order" means for a value that is not a number.
        kept.Sort(static (left, right) => string.CompareOrdinal(
            left.Attribute("Id")?.Value ?? string.Empty,
            right.Attribute("Id")?.Value ?? string.Empty));

        // Written out and read back, so that the namespace the transform's own document is in
        // is an attribute of it. Canonicalisation reads declarations rather than inferring
        // them, which is true of every document that came from a file and not of one built
        // here.
        var built = new XDocument(new XElement(Package + "Relationships", kept));
        return CanonicalXml.Parse(built.ToString(SaveOptions.DisableFormatting));
    }

    /// <summary>
    /// Applies the parts of the MCE processing model that can affect the root's effective
    /// children. The Relationships namespace is the only understood application namespace.
    /// </summary>
    private static List<XElement>? EffectiveChildren(XElement parent, MceContext inherited)
    {
        if (Extend(parent, inherited) is not { } context || !MustUnderstandConforms(parent) ||
            !AttributesConform(parent, context))
        {
            return null;
        }

        var result = new List<XElement>();
        foreach (XElement child in parent.Elements())
        {
            if (!AppendEffective(child, context, result))
                return null;
        }

        return result;
    }

    private static bool AppendEffective(XElement element, MceContext inherited, List<XElement> result)
    {
        if (Extend(element, inherited) is not { } context)
            return false;

        if (element.Name == Mce + "AlternateContent")
            return MustUnderstandConforms(element) && AppendAlternateContent(element, context, result);

        string namespaceName = element.Name.NamespaceName;
        if (namespaceName == Package.NamespaceName)
        {
            if (!MustUnderstandConforms(element) || !AttributesConform(element, context))
                return false;

            result.Add(element);
            return true;
        }

        if (!context.Ignorable.Contains(namespaceName))
            return false;

        if (!context.ProcessContent.Contains((namespaceName, element.Name.LocalName)) &&
            !context.ProcessContent.Contains((namespaceName, "*")))
        {
            return true;
        }

        XNamespace xml = XNamespace.Xml;
        if (!MustUnderstandConforms(element) || element.Attribute(xml + "base") is not null ||
            element.Attribute(xml + "lang") is not null || element.Attribute(xml + "space") is not null)
        {
            return false;
        }

        foreach (XElement child in element.Elements())
        {
            if (!AppendEffective(child, context, result))
                return false;
        }

        return true;
    }

    private static bool AppendAlternateContent(
        XElement alternate, MceContext context, List<XElement> result)
    {
        foreach (XElement child in alternate.Elements())
        {
            if (child.Name != Mce + "Choice" && child.Name != Mce + "Fallback" &&
                !IsIgnored(child, context))
            {
                return false;
            }
        }

        XElement? selected = null;
        foreach (XElement choice in alternate.Elements(Mce + "Choice"))
        {
            if (Extend(choice, context) is not { } choiceContext)
                return false;

            string[] required = Tokens(choice.Attribute("Requires")?.Value);
            if (required.Length == 0)
                return false;

            bool understood = true;
            foreach (string prefix in required)
            {
                if (choice.GetNamespaceOfPrefix(prefix) is not { } namespaceName)
                    return false;

                if (namespaceName.NamespaceName != Package.NamespaceName)
                {
                    understood = false;
                    break;
                }
            }

            if (understood)
            {
                if (!MustUnderstandConforms(choice))
                    return false;

                selected = choice;
                context = choiceContext;
                break;
            }
        }

        if (selected is null)
        {
            selected = alternate.Elements(Mce + "Fallback").FirstOrDefault();
            if (selected is null)
                return true;

            if (Extend(selected, context) is not { } fallbackContext || !MustUnderstandConforms(selected))
                return false;

            context = fallbackContext;
        }

        foreach (XElement child in selected.Elements())
        {
            if (!AppendEffective(child, context, result))
                return false;
        }

        return true;
    }

    private static bool IsIgnored(XElement element, MceContext inherited)
    {
        if (Extend(element, inherited) is not { } context)
            return false;

        string namespaceName = element.Name.NamespaceName;
        return context.Ignorable.Contains(namespaceName) &&
            !context.ProcessContent.Contains((namespaceName, element.Name.LocalName)) &&
            !context.ProcessContent.Contains((namespaceName, "*"));
    }

    /// <summary>Adds the MCE declarations made by one element to its inherited context.</summary>
    private static MceContext? Extend(XElement element, MceContext inherited)
    {
        var ignorable = new HashSet<string>(inherited.Ignorable, StringComparer.Ordinal);
        var processContent = new HashSet<(string Namespace, string Local)>(inherited.ProcessContent);

        foreach (string prefix in Tokens(element.Attribute(Mce + "Ignorable")?.Value))
        {
            if (element.GetNamespaceOfPrefix(prefix) is not { } namespaceName || namespaceName == Mce)
                return null;

            ignorable.Add(namespaceName.NamespaceName);
        }

        foreach (string token in Tokens(element.Attribute(Mce + "ProcessContent")?.Value))
        {
            int colon = token.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || colon == token.Length - 1 ||
                element.GetNamespaceOfPrefix(token[..colon]) is not { } namespaceName ||
                !ignorable.Contains(namespaceName.NamespaceName))
            {
                return null;
            }

            processContent.Add((namespaceName.NamespaceName, token[(colon + 1)..]));
        }

        return new MceContext(ignorable, processContent);
    }

    private static bool MustUnderstandConforms(XElement element)
    {
        foreach (string prefix in Tokens(element.Attribute(Mce + "MustUnderstand")?.Value))
        {
            if (element.GetNamespaceOfPrefix(prefix)?.NamespaceName != Package.NamespaceName)
                return false;
        }

        return true;
    }

    private static bool AttributesConform(XElement element, MceContext context)
    {
        foreach (XAttribute attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name.NamespaceName.Length == 0 ||
                attribute.Name.Namespace == Mce || attribute.Name.Namespace == Package ||
                context.Ignorable.Contains(attribute.Name.NamespaceName))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static string[] Tokens(string? value) => value?.Split(
        (char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];

    /// <summary>
    /// One relationship as the transform writes it: the four attributes it keeps, with the two
    /// that have defaults written out rather than left to be assumed.
    /// </summary>
    private static XElement Normalise(XElement relationship)
    {
        var copy = new XElement(Package + "Relationship");
        foreach (string name in (string[])["Id", "Type", "Target"])
        {
            if (relationship.Attribute(name)?.Value is { } value)
                copy.SetAttributeValue(name, value);
        }

        copy.SetAttributeValue("TargetMode", relationship.Attribute("TargetMode")?.Value ?? "Internal");
        return copy;
    }

    private sealed record MceContext(
        HashSet<string> Ignorable,
        HashSet<(string Namespace, string Local)> ProcessContent)
    {
        public static MceContext Empty { get; } = new(
            new HashSet<string>(StringComparer.Ordinal), []);
    }
}

/// <summary>What a relationships transform selects out of a part.</summary>
/// <param name="Ids">The identifiers it names, if any.</param>
/// <param name="Types">The relationship types it names, if any.</param>
internal readonly record struct RelationshipSelection(IReadOnlyList<string> Ids, IReadOnlyList<string> Types)
{
    /// <summary>Whether one relationship is inside the selection.</summary>
    /// <param name="id">Its identifier.</param>
    /// <param name="type">Its relationship type.</param>
    public bool Covers(string? id, string? type) =>
        (id is not null && Ids.Any(candidate => AsciiEquals(candidate, id)))
        || (type is not null && Types.Any(candidate => AsciiEquals(candidate, type)));

    private static bool AsciiEquals(string left, string right)
    {
        if (left.Length != right.Length)
            return false;

        for (int index = 0; index < left.Length; index++)
        {
            char a = left[index];
            char b = right[index];
            if (a is >= 'A' and <= 'Z')
                a = (char)(a + ('a' - 'A'));
            if (b is >= 'A' and <= 'Z')
                b = (char)(b + ('a' - 'A'));
            if (a != b)
                return false;
        }

        return true;
    }

    /// <summary>Reads what a transform element names.</summary>
    /// <param name="transform">The <c>Transform</c> element of the reference.</param>
    public static RelationshipSelection Of(XElement transform)
    {
        ArgumentNullException.ThrowIfNull(transform);

        var ids = new List<string>();
        var types = new List<string>();

        foreach (XElement child in transform.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "RelationshipReference" when child.Attribute("SourceId")?.Value is { } id:
                    ids.Add(id);
                    break;
                case "RelationshipsGroupReference" when child.Attribute("SourceType")?.Value is { } type:
                    types.Add(type);
                    break;
            }
        }

        return new RelationshipSelection(ids, types);
    }
}
