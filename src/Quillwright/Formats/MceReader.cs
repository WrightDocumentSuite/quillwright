using System.Xml;
using Quillwright.Model;

namespace Quillwright.Formats;

/// <summary>
/// Resolves a run-level <c>mc:AlternateContent</c> block: picks the branch a reader of this
/// vocabulary sees, and models its content while keeping every branch verbatim.
/// </summary>
/// <remarks>
/// <para>
/// Without this, a picture Word wrapped in a compatibility block round-trips perfectly and
/// is invisible: it never becomes a <see cref="Picture"/>, so the media API cannot find it
/// and resizing it is impossible. Selection follows ISO/IEC 29500-3 §9.3 — the first
/// <c>mc:Choice</c> whose <c>Requires</c> prefixes all name vocabularies this library
/// understands, otherwise the <c>mc:Fallback</c>.
/// </para>
/// <para>
/// Only the selected branch's content is modelled, and the markup around it is sliced out of
/// the original bytes rather than rebuilt, so a document that is loaded and saved unchanged
/// keeps its alternatives byte for byte. Anything less tidy than a single <c>w:drawing</c>
/// holding one picture is left as a verbatim fragment, exactly as before.
/// </para>
/// </remarks>
internal static partial class MceReader
{
    /// <summary>Reads an <c>mc:AlternateContent</c> element captured verbatim.</summary>
    /// <param name="markup">The whole element, as read from the part.</param>
    /// <param name="context">The load in progress, used to resolve the image part.</param>
    /// <param name="scope">
    /// The namespace declarations in scope where the element was captured, which is where a
    /// <c>Requires</c> prefix is usually bound.
    /// </param>
    /// <returns>
    /// An <see cref="AlternateContent"/> when the selected branch is one modelled picture,
    /// otherwise the markup preserved as a <see cref="RawInline"/>.
    /// </returns>
    public static InlineObject Read(string markup, LoadContext context, IDictionary<string, string> scope)
    {
        if (ReadBranches(markup, scope) is not { } branches)
            return new RawInline(markup);

        int selected = Select(branches);
        if (selected < 0 || branches[selected].DrawingXml is not { } drawing)
            return new RawInline(markup);

        if (DrawingReader.Parse(drawing, context) is not { } picture)
            return new RawInline(markup);

        int at = Locate(markup, drawing, PrecedingCopies(branches, selected, drawing));
        return at < 0
            ? new RawInline(markup)
            : new AlternateContent(markup[..at], picture, markup[(at + drawing.Length)..]);
    }

    /// <summary>
    /// Which branch a reader of this vocabulary takes, for a compatibility block whose content
    /// is read by somebody else — the block-level one, whose branches hold whole paragraphs.
    /// </summary>
    /// <param name="markup">The whole element, as read from the part.</param>
    /// <param name="scope">The namespace declarations in scope where it was captured.</param>
    /// <returns>
    /// Its index among the branches, or <see langword="null"/> when none applies or the element
    /// holds something other than branches and is better left alone.
    /// </returns>
    public static int? Selected(string markup, IDictionary<string, string> scope)
    {
        if (ReadBranches(markup, scope) is not { } branches)
            return null;

        int selected = Select(branches);
        return selected < 0 ? null : selected;
    }

    /// <summary>
    /// The <c>mc:Choice</c> and <c>mc:Fallback</c> children in order, or <see langword="null"/>
    /// when the element holds anything else and is better left alone.
    /// </summary>
    private static List<Branch>? ReadBranches(string markup, IDictionary<string, string> scope)
    {
        using var xml = XmlReader.Create(new StringReader(markup), Xml.XmlDefaults.ReaderSettings);
        if (xml.MoveToContent() != XmlNodeType.Element ||
            xml.LocalName != "AlternateContent" || xml.NamespaceURI != DocxSchema.NsMarkupCompatibility)
            return null;

        if (WithIgnorable(xml, scope, []) is not { } ignorable)
            return null;

        var branches = new List<Branch>();
        bool foreign = false;
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (reader.NamespaceURI != DocxSchema.NsMarkupCompatibility || name is not ("Choice" or "Fallback"))
            {
                if (!TryIgnored(reader, scope, ignorable, out bool ignored) || !ignored)
                    foreign = true;

                reader.Skip();
                return;
            }

            branches.Add(ReadBranch(reader, isChoice: name == "Choice", scope, ignorable));
        });

        return foreign || branches.Exists(static branch => !branch.Conformant) ? null : branches;
    }

    /// <summary>Reads one branch, noting whether it applies and what single drawing it holds.</summary>
    private static Branch ReadBranch(
        XmlReader xml, bool isChoice, IDictionary<string, string> scope, HashSet<string> inheritedIgnorable)
    {
        // A Fallback applies whenever no earlier Choice did, so only a Choice is questioned —
        // and the question has to be asked before the reader leaves the start tag behind.
        bool applies = !isChoice || Applies(xml, scope);
        HashSet<string>? ignorable = WithIgnorable(xml, scope, inheritedIgnorable);
        string? drawing = null;
        int children = 0;
        bool conformant = ignorable is not null;

        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (!conformant || !TryIgnored(reader, scope, ignorable!, out bool ignored))
            {
                conformant = false;
                reader.Skip();
                return;
            }

            if (ignored)
            {
                reader.Skip();
                return;
            }

            children++;
            if (children == 1 && name == "drawing" && DocxSchema.IsWordNamespace(reader.NamespaceURI))
                drawing = reader.ReadOuterXml();
            else
                reader.Skip();
        });

        return new Branch(isChoice, applies, children == 1 ? drawing : null, conformant);
    }

    /// <summary>Whether every vocabulary a <c>mc:Choice</c> requires is one this library understands.</summary>
    private static bool Applies(XmlReader xml, IDictionary<string, string> scope)
    {
        // The attribute is unqualified and mandatory (ISO/IEC 29500-3 §7.6); a Choice without
        // one is not conformant, and passing over it leaves the fallback to be taken.
        if (xml.GetAttribute("Requires") is not { } required)
            return false;

        foreach (string prefix in required.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            // A declaration inside the captured fragment shadows the one it was captured under.
            if (Resolve(xml, scope, prefix) is not { } uri || !Understands(uri))
                return false;
        }

        return true;
    }

    private static string? Resolve(XmlReader xml, IDictionary<string, string> scope, string prefix) =>
        xml.LookupNamespace(prefix) ?? (scope.TryGetValue(prefix, out string? uri) ? uri : null);

    /// <summary>Whether a required vocabulary is one this library interprets.</summary>
    /// <remarks>
    /// ISO/IEC 29500-3 defines an understood namespace by processing capability, not by whether
    /// its markup can be preserved. The list is therefore intentionally explicit: a future
    /// namespace under an otherwise familiar URI base must take the fallback until a reader for
    /// its semantics exists.
    /// </remarks>
    private static bool Understands(string namespaceUri) =>
        namespaceUri is
            DocxSchema.NsWord or DocxSchema.NsWordStrict or
            DocxSchema.NsRelationships or DocxSchema.NsRelationshipsStrict or
            DocxSchema.NsDrawing or DocxSchema.NsWordDrawing or DocxSchema.NsPicture or
            DocxSchema.NsChart or DocxSchema.NsChartStrict or DocxSchema.NsWordShape or
            DocxSchema.NsMath or DocxSchema.NsMathStrict or
            DocxSchema.NsVml or DocxSchema.NsOffice or DocxSchema.NsWord10;

    /// <summary>Adds the ignorable namespaces declared on an element to those it inherited.</summary>
    private static HashSet<string>? WithIgnorable(
        XmlReader xml, IDictionary<string, string> scope, IEnumerable<string> inherited)
    {
        var result = new HashSet<string>(inherited, StringComparer.Ordinal);
        if (xml.GetAttribute("Ignorable", DocxSchema.NsMarkupCompatibility) is not { } value)
            return result;

        foreach (string prefix in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            string? uri = Resolve(xml, scope, prefix);
            if (uri is null || uri == DocxSchema.NsMarkupCompatibility)
                return null;

            result.Add(uri);
        }

        return result;
    }

    /// <summary>Whether an extension element is removed by the MCE processing model.</summary>
    private static bool TryIgnored(
        XmlReader xml, IDictionary<string, string> scope, HashSet<string> inherited, out bool ignored)
    {
        if (WithIgnorable(xml, scope, inherited) is not { } inScope)
        {
            ignored = false;
            return false;
        }

        ignored = inScope.Contains(xml.NamespaceURI) && !Understands(xml.NamespaceURI);
        return true;
    }

    /// <summary>Index of the branch that applies, or -1 when none does.</summary>
    private static int Select(List<Branch> branches)
    {
        for (int i = 0; i < branches.Count; i++)
        {
            if (branches[i] is { IsChoice: true, Applies: true })
                return i;
        }

        return branches.FindIndex(static branch => !branch.IsChoice);
    }

    /// <summary>
    /// Where the selected branch's drawing sits in the original markup, skipping the earlier
    /// branches that spell the very same drawing (a chart and its fallback often do).
    /// </summary>
    private static int Locate(string markup, string drawing, int skip)
    {
        int at = markup.IndexOf(drawing, StringComparison.Ordinal);
        while (at >= 0 && skip-- > 0)
            at = markup.IndexOf(drawing, at + 1, StringComparison.Ordinal);
        return at;
    }

    private static int PrecedingCopies(List<Branch> branches, int selected, string drawing)
    {
        int copies = 0;
        for (int i = 0; i < selected; i++)
        {
            if (branches[i].DrawingXml == drawing)
                copies++;
        }

        return copies;
    }

    private readonly record struct Branch(bool IsChoice, bool Applies, string? DrawingXml, bool Conformant);
}
