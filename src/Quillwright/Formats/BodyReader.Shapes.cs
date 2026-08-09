using System.Xml;
using Quillwright.Model;

namespace Quillwright.Formats;

/// <summary>
/// Recognises a shape with words in it — a text box — and models the words while keeping the
/// shape itself as the bytes it arrived as.
/// </summary>
internal sealed partial class BodyReader
{
    private const string TextBoxElement = "txbxContent";

    /// <summary>
    /// Reads shape markup as a <see cref="Shape"/> when it holds text, otherwise as a
    /// verbatim fragment.
    /// </summary>
    /// <param name="markup">The whole element: a drawing, a VML picture, or a compatibility block.</param>
    /// <param name="scope">The namespace declarations in force where the markup was captured.</param>
    /// <remarks>
    /// The markup around the content is cut out of the original bytes and written back
    /// untouched, so everything about the shape survives whether or not it is understood. What
    /// is understood — how big it is, where it sits, what it is painted in — is read off the
    /// same bytes and offered as properties, which is what lets a renderer draw it. Word writes
    /// the same words twice, once in the modern branch and once in the fallback an older reader
    /// uses, and when the two copies arrive identical the model replaces both with one — editing
    /// the text then cannot leave the fallback saying something else.
    /// </remarks>
    private InlineObject ReadShape(string markup, IDictionary<string, string> scope)
    {
        List<Range> slots = FindTextBoxes(markup);
        if (slots.Count == 0)
        {
            if (ReadChartFrame(markup) is { } chart)
                return chart;

            DrawingGeometry primitive = DrawingGeometry.Read(markup);
            if (!primitive.IsLine)
                return new RawInline(markup);

            return new Shape([markup], new TextBox())
            {
                Width = Primitives.Length.FromEmu(primitive.Width),
                Height = Primitives.Length.FromEmu(primitive.Height),
                IsInline = primitive.IsInline || !primitive.IsAnchored,
                Anchor = primitive.Anchor,
                Outline = primitive.Outline,
                IsLine = true,
            };
        }

        // Two copies that already differ are two different things, and rewriting both from one
        // model would lose whichever was not read.
        string first = markup[slots[0]];
        if (slots.Count > 1 && slots.Any(slot => !markup[slot].Equals(first, StringComparison.Ordinal)))
            slots.RemoveRange(1, slots.Count - 1);

        var content = new TextBox();
        if (!ReadTextBoxBlocks(first, scope, content) || content.Blocks.Count == 0)
            return new RawInline(markup);

        DrawingGeometry geometry = DrawingGeometry.Read(markup);
        return new Shape(Cut(markup, slots), content)
        {
            Width = Primitives.Length.FromEmu(geometry.Width),
            Height = Primitives.Length.FromEmu(geometry.Height),
            IsInline = geometry.IsInline || !geometry.IsAnchored,
            Anchor = geometry.Anchor,
            Fill = geometry.Fill,
            Outline = geometry.Outline,
            Direction = geometry.TextFlow,
            InsetLeft = geometry.TextInsetLeft,
            InsetRight = geometry.TextInsetRight,
            InsetTop = geometry.TextInsetTop,
            InsetBottom = geometry.TextInsetBottom,
        };
    }

    /// <summary>
    /// Recognises a drawing that reserves room for a chart, so that a renderer can find both the
    /// frame and the part it draws.
    /// </summary>
    /// <remarks>
    /// The markup is kept whole either way; what this adds is the size, the anchor and the name
    /// of the part, which is the least a caller needs to draw the chart where the document puts
    /// it. A chart whose relationship does not resolve is still a frame, with nothing in it.
    /// </remarks>
    /// <param name="markup">The whole drawing element.</param>
    private ChartFrame? ReadChartFrame(string markup)
    {
        if (!markup.Contains(":chart ", StringComparison.Ordinal) && !markup.Contains(":chart/", StringComparison.Ordinal))
            return null;

        DrawingGeometry geometry = DrawingGeometry.Read(markup);
        if (geometry.ChartRelationshipId is null)
            return null;

        return new ChartFrame(markup, _context.PartFor(geometry.ChartRelationshipId))
        {
            Width = Primitives.Length.FromEmu(geometry.Width),
            Height = Primitives.Length.FromEmu(geometry.Height),
            IsInline = geometry.IsInline || !geometry.IsAnchored,
            Anchor = geometry.Anchor,
        };
    }

    /// <summary>The markup between the content slots, one piece more than there are slots.</summary>
    private static List<string> Cut(string markup, List<Range> slots)
    {
        var fragments = new List<string>(slots.Count + 1);
        int cursor = 0;
        foreach (Range slot in slots)
        {
            (int start, int length) = slot.GetOffsetAndLength(markup.Length);
            fragments.Add(markup[cursor..start]);
            cursor = start + length;
        }

        fragments.Add(markup[cursor..]);
        return fragments;
    }

    /// <summary>
    /// Reads a block-level <c>mc:AlternateContent</c> (ISO/IEC 29500-3 §9.3): the blocks of the
    /// branch this vocabulary selects, with the markup either side of them kept verbatim.
    /// </summary>
    /// <remarks>
    /// A branch this reader cannot parse on its own, or one holding nothing block-level, leaves
    /// the whole element preserved — which is what it was before, and loses nothing.
    /// </remarks>
    /// <param name="xml">Reader positioned on the <c>mc:AlternateContent</c> element.</param>
    private Block ReadAlternateBlock(XmlReader xml)
    {
        IDictionary<string, string> scope = XmlHelp.NamespacesInScope(xml);
        string markup = xml.ReadOuterXml();

        if (MceReader.Selected(markup, scope) is not { } selected)
            return new RawBlock(markup);

        List<Range> branches = MceReader.BranchRanges(markup);
        if (selected >= branches.Count)
            return new RawBlock(markup);

        (int start, int length) = branches[selected].GetOffsetAndLength(markup.Length);
        var block = new AlternateContentBlock(markup[..start], markup[(start + length)..]);

        return ReadFragmentBlocks(markup.Substring(start, length), scope, block.Content) && block.Blocks.Count > 0
            ? block
            : new RawBlock(markup);
    }

    /// <summary>
    /// Reads the blocks of one text box, under a root that re-declares the namespaces the
    /// markup was written against so that a prefix used inside it still resolves.
    /// </summary>
    private bool ReadTextBoxBlocks(string inner, IDictionary<string, string> scope, TextBox content) =>
        ReadFragmentBlocks(inner, scope, content);

    /// <summary>
    /// Reads block-level markup captured out of a larger part, under a root that re-declares
    /// the namespaces it was written against so that a prefix used inside it still resolves.
    /// </summary>
    /// <param name="inner">The captured markup.</param>
    /// <param name="scope">The namespace declarations in force where it was captured.</param>
    /// <param name="content">Where the blocks go.</param>
    private bool ReadFragmentBlocks(string inner, IDictionary<string, string> scope, BlockContainer content)
    {
        var root = new System.Text.StringBuilder("<q:root");
        Declare(root, "q", DocxSchema.NsWord);
        foreach ((string prefix, string uri) in scope)
        {
            if (prefix.Length > 0 && prefix != "q" && prefix != "xml")
                Declare(root, prefix, uri);
        }

        root.Append('>').Append(inner).Append("</q:root>");

        try
        {
            using var xml = XmlReader.Create(new StringReader(root.ToString()), Xml.XmlDefaults.ReaderSettings);
            if (xml.MoveToContent() == XmlNodeType.Element)
                ReadBlocks(xml, content);
            return true;
        }
        catch (XmlException)
        {
            // Markup this reader cannot parse on its own is left whole rather than guessed at.
            return false;
        }
    }

    private static void Declare(System.Text.StringBuilder root, string prefix, string uri) =>
        root.Append(" xmlns:").Append(prefix).Append("=\"").Append(System.Security.SecurityElement.Escape(uri)).Append('"');

    /// <summary>
    /// Where the content of each <c>w:txbxContent</c> sits inside the markup.
    /// </summary>
    /// <remarks>
    /// The tags are found in the text rather than by re-serializing the elements, because
    /// serializing a fragment on its own adds the namespace declarations its ancestors used to
    /// supply and the result would no longer be found in the original. The scan is checked
    /// against what the parser sees, and disagreement means the markup is left alone.
    /// </remarks>
    private static List<Range> FindTextBoxes(string markup)
    {
        var slots = new List<Range>();
        int cursor = 0;
        while (NextStartTag(markup, cursor) is var open && open >= 0)
        {
            int inner = EndOfTag(markup, open);
            if (inner < 0)
                return [];

            // An empty element has no content to model, but still has to be stepped over.
            if (markup[inner - 2] == '/')
            {
                cursor = inner;
                continue;
            }

            int close = NextEndTag(markup, inner);
            if (close < 0)
                return [];

            slots.Add(new Range(inner, close));
            cursor = EndOfTag(markup, close);
            if (cursor < 0)
                return [];
        }

        return slots.Count == CountTextBoxes(markup) ? slots : [];
    }

    /// <summary>How many text boxes the parser sees, as a check on the scan that found them.</summary>
    private static int CountTextBoxes(string markup)
    {
        int count = 0;
        using var xml = XmlReader.Create(new StringReader(markup), Xml.XmlDefaults.ReaderSettings);
        while (xml.Read())
        {
            if (xml.NodeType == XmlNodeType.Element && xml.LocalName == TextBoxElement &&
                DocxSchema.IsWordNamespace(xml.NamespaceURI) && !xml.IsEmptyElement)
                count++;
        }

        return count;
    }

    private static int NextStartTag(string markup, int from) => NextTag(markup, from, "<");

    private static int NextEndTag(string markup, int from) => NextTag(markup, from, "</");

    /// <summary>The next tag of the text-box element, whatever prefix it was written with.</summary>
    private static int NextTag(string markup, int from, string opener)
    {
        for (int at = markup.IndexOf(opener, from, StringComparison.Ordinal); at >= 0;
             at = markup.IndexOf(opener, at + 1, StringComparison.Ordinal))
        {
            int name = at + opener.Length;
            int colon = markup.IndexOf(':', name);
            int start = colon > name && colon < name + 32 ? colon + 1 : name;
            if (!markup.AsSpan(start).StartsWith(TextBoxElement, StringComparison.Ordinal))
                continue;

            char after = markup.Length > start + TextBoxElement.Length ? markup[start + TextBoxElement.Length] : '\0';
            if (after is '>' or '/' or ' ' or '\t' or '\r' or '\n')
                return at;
        }

        return -1;
    }

    /// <summary>
    /// The index just past the tag that starts at <paramref name="from"/>. Attribute values are
    /// quoted, so the scan steps over any angle bracket inside one.
    /// </summary>
    private static int EndOfTag(string markup, int from)
    {
        char quote = '\0';
        for (int i = from; i < markup.Length; i++)
        {
            char c = markup[i];
            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i + 1;
            }
        }

        return -1;
    }
}
