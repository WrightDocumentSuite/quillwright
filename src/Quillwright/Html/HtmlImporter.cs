using System.Globalization;
using Quillwright.Diagnostics;
using Quillwright.IO;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Html;

/// <summary>
/// Turns HTML into a document: the elements that mean something in Word become the real thing
/// — headings, lists on real numbering, tables with their spans as merges, hyperlinks,
/// pictures — and inline CSS is read for what Word can also say. The mapping mirrors the HTML
/// exporter's, so a page that came from a document imports back to the same constructs.
/// </summary>
/// <remarks>
/// The markup is parsed by <see cref="HtmlParser"/>, which implements the standard's parsing
/// algorithm rather than approximating it, so whatever a browser makes of an author's markup
/// is what this maps. What has no Word counterpart — a script, a form, a frame — is left out
/// or unwrapped, and every such decision is named in the diagnostics with its line.
/// </remarks>
public static class HtmlImporter
{
    /// <summary>Imports HTML into a new document.</summary>
    /// <param name="html">The HTML source, a full page or a fragment.</param>
    /// <param name="options">How to import it, or <see langword="null"/> for the defaults.</param>
    public static HtmlImportResult Import(string html, HtmlImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);

        var context = new ImportContext(options ?? new HtmlImportOptions());
        context.Budget.ValidateText(html);
        HtmlElement root = HtmlParser.Parse(html, context.Budget);

        if (FindElement(root, "title") is { } title)
            context.Document.Properties.Title = NormalizeWhitespace(PlainText(title)).Trim(' ');

        HtmlElement body = FindElement(root, "body") ?? root;
        var blocks = new BlockTarget(context.Document.Sections[0].Blocks);
        MapBlocks(body, context, blocks, new Inherited());
        blocks.Flush();

        if (context.Document.Sections[0].Blocks.Count == 0)
            context.Document.Sections[0].AddParagraph(string.Empty);

        return new HtmlImportResult(context.Document, context.Diagnostics);
    }

    /// <summary>Imports an HTML fragment using the supplied HTML element as its parsing context.</summary>
    /// <param name="html">The fragment markup.</param>
    /// <param name="contextElement">
    /// The local name that selects the tokenizer state and tree-construction insertion mode.
    /// </param>
    /// <param name="options">How to import it, or <see langword="null"/> for the defaults.</param>
    /// <remarks>
    /// The context element itself is not included in the result. For example, a
    /// <c>textarea</c> context treats tags as text, while a <c>table</c> context creates the
    /// table children browsers imply for the same fragment.
    /// </remarks>
    public static HtmlImportResult ImportFragment(
        string html,
        string contextElement = "body",
        HtmlImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentException.ThrowIfNullOrEmpty(contextElement);

        var context = new ImportContext(options ?? new HtmlImportOptions());
        context.Budget.ValidateText(html);
        HtmlElement fragment = HtmlParser.ParseFragment(html, contextElement, budget: context.Budget);
        var blocks = new BlockTarget(context.Document.Sections[0].Blocks);
        MapBlocks(fragment, context, blocks, new Inherited());
        blocks.Flush();

        if (context.Document.Sections[0].Blocks.Count == 0)
            context.Document.Sections[0].AddParagraph(string.Empty);

        return new HtmlImportResult(context.Document, context.Diagnostics);
    }

    /// <summary>Reads an HTML file and imports it, resolving images beside the file.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="options">
    /// How to import it; when no media directory is set, the file's own directory is used.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<HtmlImportResult> ImportFileAsync(
        string path, HtmlImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        options ??= new HtmlImportOptions();
        byte[] bytes = await DocumentInput.ReadFileBytesAsync(path, options.Budget, cancellationToken).ConfigureAwait(false);
        string html = HtmlEncoding.Decode(bytes);
        if (options.MediaDirectory is null)
            options = options with { MediaDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) };

        return Import(html, options);
    }

    private sealed class ImportContext
    {
        public ImportContext(HtmlImportOptions options)
        {
            Options = options;
            Budget = new DocumentLoadBudgetState(options.Budget);
            Document = WordDocument.Create();
            Numbering = new NumberingBuilder(Document.Numbering);
        }

        public HtmlImportOptions Options { get; }

        public DocumentLoadBudgetState Budget { get; }

        public WordDocument Document { get; }

        public NumberingBuilder Numbering { get; }

        public HtmlImportDiagnostics Diagnostics { get; } = new();

        public int NextBookmarkId { get; set; } = 1;

        public ImageData? ResolveImage(string source, int line)
        {
            if (!Options.ImportImages)
                return null;

            if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = source.IndexOf(',', StringComparison.Ordinal);
                if (comma < 0 || !source.AsSpan(0, comma).Contains(";base64", StringComparison.OrdinalIgnoreCase))
                {
                    Diagnostics.Add(HtmlImportWarningKind.ImageSkipped, "Only a base64 data URI can be decoded.", null, line);
                    return null;
                }

                try
                {
                    ReadOnlySpan<char> encoded = source.AsSpan(comma + 1);
                    Budget.EnsureMedia(EstimatedBase64Bytes(encoded));
                    byte[] bytes = Convert.FromBase64String(source[(comma + 1)..]);
                    Budget.AddMedia(bytes.LongLength);
                    return Document.Media.Add(ImageData.FromBytes(bytes));
                }
                catch (FormatException)
                {
                    Diagnostics.Add(HtmlImportWarningKind.ImageSkipped, "The data URI is not valid base64.", null, line);
                    return null;
                }
            }

            if (source.Contains("://", StringComparison.Ordinal))
            {
                Diagnostics.Add(
                    HtmlImportWarningKind.ImageSkipped,
                    "A remote image is not fetched — nothing here opens a network connection — so its alternative text stands in for it.",
                    source, line);
                return null;
            }

            if (Options.MediaDirectory is null)
            {
                Diagnostics.Add(
                    HtmlImportWarningKind.ImageSkipped,
                    "No media directory was given, so a relative image path cannot be resolved.",
                    source, line);
                return null;
            }

            if (Budget.MaximumNextMediaBytes < 1)
            {
                throw new DocumentLoadLimitException(
                    nameof(DocumentLoadBudget.MaxTotalMediaBytes),
                    Budget.Budget.MaxTotalMediaBytes,
                    Budget.Budget.MaxTotalMediaBytes + 1);
            }

            MediaFileReadResult file = MediaFileResolver.Read(
                Options.MediaDirectory, source, Budget.MaximumNextMediaBytes);
            if (file.Status == MediaFileReadStatus.Unsafe)
            {
                Diagnostics.Add(
                    HtmlImportWarningKind.ImageSkipped,
                    "A rooted image path, a traversal segment or a symbolic link is not followed.",
                    source, line);
                return null;
            }

            if (file.Status == MediaFileReadStatus.Missing)
            {
                Diagnostics.Add(HtmlImportWarningKind.ImageSkipped, "The image file does not exist.", source, line);
                return null;
            }

            if (file.Status == MediaFileReadStatus.Unreadable)
            {
                Diagnostics.Add(HtmlImportWarningKind.ImageSkipped, "The image file could not be read.", source, line);
                return null;
            }

            if (file.Status == MediaFileReadStatus.TooLarge)
            {
                Budget.EnsureMedia(file.Length);
                Diagnostics.Add(HtmlImportWarningKind.ImageSkipped, "The image file is too large to read.", source, line);
                return null;
            }

            Budget.AddMedia(file.Bytes!.LongLength);
            return Document.Media.Add(ImageData.FromBytes(file.Bytes!));
        }

        private static long EstimatedBase64Bytes(ReadOnlySpan<char> encoded)
        {
            long characters = 0;
            int padding = 0;
            for (int i = 0; i < encoded.Length; i++)
            {
                char character = encoded[i];
                if (char.IsWhiteSpace(character))
                    continue;
                characters++;
                padding = character == '=' ? padding + 1 : 0;
            }

            long bytes = ((characters + 3) / 4) * 3 - Math.Min(padding, 2);
            return Math.Max(0, bytes);
        }
    }

    /// <summary>What the surrounding elements have already decided about the text inside.</summary>
    private readonly record struct Inherited
    {
        public RunFormat Format { get; init; }

        public string? StyleId { get; init; }

        public int? NumberingId { get; init; }

        public int ListLevel { get; init; }

        public string? ListStyleType { get; init; }

        public bool ListStyleTypeSpecified { get; init; }

        public bool Preformatted { get; init; }

        public ParagraphAlignment? Alignment { get; init; }

        public Inherited()
        {
            Format = RunFormat.Default;
            ListLevel = -1;
        }
    }

    /// <summary>
    /// Where blocks land, with the paragraph under construction: inline content accumulates
    /// into one paragraph until a block boundary flushes it.
    /// </summary>
    private sealed class BlockTarget(IList<Block> blocks)
    {
        private Paragraph? _open;
        private bool _pendingSpace;
        private bool _numbered;

        public IList<Block> Blocks { get; } = blocks;

        public bool HasOpenContent => _open is { IsEmpty: false };

        public Paragraph Open(in Inherited inherited)
        {
            if (_open is null)
            {
                _open = new Paragraph();
                _pendingSpace = false;
                if (inherited.StyleId is { } style)
                    _open.Format = _open.Format with { StyleId = style };
                if (inherited.Alignment is { } alignment)
                    _open.Format = _open.Format with { Alignment = alignment };
                if (inherited.NumberingId is { } list && !_numbered)
                {
                    _open.Format = _open.Format with
                    {
                        NumberingId = list,
                        NumberingLevel = Math.Clamp(inherited.ListLevel, 0, 8),
                    };
                    _numbered = true;
                }
            }

            return _open;
        }

        public void AppendText(string text, in Inherited inherited)
        {
            if (inherited.Preformatted)
            {
                AppendPreformatted(text, inherited);
                return;
            }

            bool leading = text.Length > 0 && IsCollapsibleWhitespace(text[0]);
            bool trailing = text.Length > 0 && IsCollapsibleWhitespace(text[^1]);
            string collapsed = NormalizeWhitespace(text).Trim(' ');

            if (collapsed.Length == 0)
            {
                _pendingSpace |= (leading || trailing) && HasOpenContent;
                return;
            }

            Paragraph paragraph = Open(inherited);
            if ((_pendingSpace || leading) && !paragraph.IsEmpty)
                paragraph.AppendText(" ", inherited.Format);

            paragraph.AppendText(collapsed, inherited.Format);
            _pendingSpace = trailing;
        }

        private void AppendPreformatted(string text, in Inherited inherited)
        {
            Paragraph paragraph = Open(inherited);
            paragraph.AppendText(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'), inherited.Format);
            _pendingSpace = false;
        }

        public void AppendBreak(in Inherited inherited)
        {
            Paragraph paragraph = Open(inherited);
            paragraph.AppendText("\n", inherited.Format);
            _pendingSpace = false;
        }

        public void Flush()
        {
            if (_open is { IsEmpty: false } paragraph)
                Blocks.Add(paragraph);

            _open = null;
            _pendingSpace = false;
            _numbered = false;
        }

        public void Add(Block block)
        {
            Flush();
            Blocks.Add(block);
        }
    }

    private static void MapBlocks(HtmlElement parent, ImportContext context, BlockTarget target, Inherited inherited)
    {
        foreach (HtmlNode node in parent.Children)
        {
            switch (node)
            {
                case HtmlText text:
                    target.AppendText(text.Value, inherited);
                    continue;

                case HtmlElement element:
                    MapElement(element, context, target, inherited);
                    continue;

                default:
                    continue;
            }
        }
    }

    private static void MapElement(HtmlElement element, ImportContext context, BlockTarget target, Inherited inherited)
    {
        switch (element.Name)
        {
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
            {
                target.Flush();
                Inherited heading = WithCss(element, context, inherited) with
                {
                    StyleId = context.Document.Styles.GetOrAdd("Heading" + element.Name[1]).Id,
                };
                MapBlocks(element, context, target, heading);
                target.Flush();
                return;
            }

            case "p" or "div" or "section" or "article" or "header" or "footer" or "main" or "figure" or "figcaption"
                or "address" or "aside" or "nav" or "details" or "summary" or "dl" or "dt" or "dd":
            {
                target.Flush();
                MapBlocks(element, context, target, WithCss(element, context, inherited));
                target.Flush();
                return;
            }

            case "blockquote":
            {
                target.Flush();
                Inherited quoted = WithCss(element, context, inherited) with
                {
                    StyleId = context.Document.Styles.GetOrAdd("Quote").Id,
                };
                MapBlocks(element, context, target, quoted);
                target.Flush();
                return;
            }

            case "pre":
            {
                target.Flush();
                Inherited code = inherited with
                {
                    Preformatted = true,
                    StyleId = context.Document.Styles.GetOrAdd("CodeBlock").Id,
                    Format = inherited.Format with { FontAscii = "Consolas", FontHighAnsi = "Consolas" },
                };
                MapBlocks(element, context, target, code);
                TrimCodeParagraph(target);
                target.Flush();
                return;
            }

            case "ul" or "ol":
                target.Flush();
                MapList(element, context, target, inherited);
                return;

            case "table":
                target.Add(MapTable(element, context));
                return;

            case "hr":
                target.Add(new Paragraph
                {
                    Format = ParagraphFormat.Default with
                    {
                        Borders = BorderSet.Empty with
                        {
                            Bottom = BorderLine.Single(Length.FromPoints(0.75), WordColor.Auto),
                        },
                    },
                });
                return;

            case "br":
                target.AppendBreak(inherited);
                return;

            case "img":
                MapImage(element, context, target, inherited);
                return;

            case "a":
                MapAnchor(element, context, target, inherited);
                return;

            case "noscript":
                MapBlocks(element, context, target, WithCss(element, context, inherited));
                return;

            case "template":
                context.Diagnostics.Add(
                    HtmlImportWarningKind.ContentSkipped,
                    "Inert template content was left out.",
                    element.Name, element.Line);
                return;

            case "script" or "style" or "iframe" or "object" or "embed" or "form" or "button" or "select"
                or "textarea" or "input" or "canvas" or "svg" or "audio" or "video":
                context.Diagnostics.Add(
                    HtmlImportWarningKind.ContentSkipped,
                    "An element with no document counterpart was left out.",
                    element.Name, element.Line);
                return;

            case "head" or "meta" or "link" or "base" or "title" or "colgroup" or "col" or "caption":
                return;

            default:
                MapInline(element, context, target, inherited);
                return;
        }
    }

    private static void MapInline(HtmlElement element, ImportContext context, BlockTarget target, Inherited inherited)
    {
        Inherited inner = element.Name switch
        {
            "strong" or "b" => inherited with { Format = inherited.Format with { Bold = true } },
            "em" or "i" or "cite" or "var" or "dfn" => inherited with { Format = inherited.Format with { Italic = true } },
            "u" => inherited with { Format = inherited.Format with { Underline = UnderlineStyle.Single } },
            "s" or "strike" => inherited with { Format = inherited.Format with { Strike = true } },
            "del" => inherited with { Format = inherited.Format with { Strike = true } },
            "ins" => inherited with { Format = inherited.Format with { Underline = UnderlineStyle.Single } },
            "sup" => inherited with { Format = inherited.Format with { VerticalAlignment = VerticalTextAlignment.Superscript } },
            "sub" => inherited with { Format = inherited.Format with { VerticalAlignment = VerticalTextAlignment.Subscript } },
            "code" or "tt" or "kbd" or "samp" => inherited with
            {
                Format = inherited.Format with { FontAscii = "Consolas", FontHighAnsi = "Consolas" },
            },
            "mark" => inherited with { Format = inherited.Format with { Highlight = HighlightColor.Yellow } },
            "small" => inherited with { Format = inherited.Format with { Size = Length.FromPoints(9) } },
            _ => inherited,
        };

        if (element.Name is "del" or "ins")
        {
            context.Diagnostics.Add(
                HtmlImportWarningKind.UnsupportedElement,
                "An edit mark is rendered as formatting rather than as a tracked change.",
                element.Name, element.Line);
        }
        else if (inner.Equals(inherited) && element.Name is not ("span" or "font" or "abbr" or "q" or "time" or "wbr" or "label" or "bdi" or "bdo" or "o:p"))
        {
            context.Diagnostics.Add(
                HtmlImportWarningKind.UnsupportedElement,
                "An element the importer does not model was unwrapped around its content.",
                element.Name, element.Line);
        }

        MapBlocks(element, context, target, WithCss(element, context, inner));
    }

    private static void MapAnchor(HtmlElement element, ImportContext context, BlockTarget target, Inherited inherited)
    {
        string? id = element.Attribute("id") ?? element.Attribute("name");
        string? href = element.Attribute("href");

        Paragraph paragraph = target.Open(inherited);
        int start = paragraph.TextLength;

        if (id is { Length: > 0 })
        {
            int bookmarkId = context.NextBookmarkId++;
            paragraph.AddMark(new BookmarkStart { Id = bookmarkId, Name = id }, start);
            MapBlocks(element, context, target, WithCss(element, context, inherited));
            paragraph.AddMark(new BookmarkEnd { Id = bookmarkId }, paragraph.TextLength);
        }
        else
        {
            MapBlocks(element, context, target, WithCss(element, context, inherited));
        }

        if (href is { Length: > 0 } && paragraph.TextLength > start)
        {
            var link = new Hyperlink { Tooltip = element.Attribute("title") };
            if (href.StartsWith('#'))
                link.Anchor = href[1..];
            else
                link.Url = href;

            paragraph.AddRange(link, start, paragraph.TextLength - start);
        }
    }

    private static void MapImage(HtmlElement element, ImportContext context, BlockTarget target, Inherited inherited)
    {
        string source = element.Attribute("src") ?? string.Empty;
        string alt = element.Attribute("alt") ?? string.Empty;

        if (context.ResolveImage(source, element.Line) is not { } image)
        {
            if (alt.Length > 0)
                target.AppendText(alt, inherited);
            return;
        }

        (Length? width, Length? height) = ImageSize(element);
        Paragraph paragraph = target.Open(inherited);
        paragraph.AppendPicture(image);

        // AppendPicture sizes from the image itself; an explicit size wins over that.
        if (paragraph.Objects.LastOrDefault().Object is Picture placed)
        {
            if (width is { } setWidth)
                placed.Width = setWidth;
            if (height is { } setHeight)
                placed.Height = setHeight;
            if (alt.Length > 0)
                placed.Description = alt;
        }
    }

    private static (Length? Width, Length? Height) ImageSize(HtmlElement element)
    {
        Length? width = Pixels(element.Attribute("width"));
        Length? height = Pixels(element.Attribute("height"));

        foreach ((string name, string value) in Css(element))
        {
            if (name == "width")
                width = CssLength(value) ?? width;
            else if (name == "height")
                height = CssLength(value) ?? height;
        }

        return (width, height);

        static Length? Pixels(string? value) =>
            value is not null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double pixels) && pixels > 0
                ? Length.FromPoints(pixels * 0.75)
                : null;
    }

    private static void MapList(HtmlElement list, ImportContext context, BlockTarget target, Inherited inherited)
    {
        Inherited styledList = WithCss(list, context, inherited);
        (AbstractNumbering baseDefinition, NumberingInstance baseInstance) = context.Numbering.AddList(
            list.Name == "ol" ? ListTemplate.Decimal : ListTemplate.Bullet);
        int instance = baseInstance.Id;
        int listLevel = Math.Clamp(inherited.ListLevel + 1, 0, 8);
        NumberingLevel definition = baseDefinition.Levels[listLevel];
        string? typeMarker = ListTypeHint(list);
        string marker = typeMarker ??
            (inherited.ListStyleTypeSpecified ? inherited.ListStyleType : null) ??
            HtmlListStyle.FromLevel(definition);
        bool markerSpecified = typeMarker is null && inherited.ListStyleTypeSpecified;
        if (TryCssListStyleType(list, inherited.ListStyleType, out string? cssMarker))
        {
            marker = cssMarker!;
            markerSpecified = true;
        }
        HtmlListStyle.Apply(definition, marker);

        List<HtmlElement> items =
            [.. list.Children.OfType<HtmlElement>().Where(static child => child.Is("li"))];
        bool ordered = list.Name == "ol";
        bool reversed = ordered && list.Attribute("reversed") is not null;
        int start = HtmlInteger(list.Attribute("start")) ?? (reversed ? items.Count : 1);
        definition.Start = start;

        Inherited itemContext = styledList with
        {
            NumberingId = instance,
            ListLevel = listLevel,
            ListStyleType = marker,
            ListStyleTypeSpecified = markerSpecified,
            StyleId = context.Document.Styles.GetOrAdd("ListParagraph").Id,
        };

        int currentInstance = instance;
        string currentMarker = marker;
        int nextValue = start;
        bool firstItem = true;
        foreach (HtmlNode node in list.Children)
        {
            if (node is not HtmlElement child)
                continue;

            if (child.Name == "li")
            {
                int? explicitValue = ordered ? HtmlInteger(child.Attribute("value")) : null;
                int value = explicitValue ?? nextValue;
                string? itemTypeMarker = ListItemTypeHint(child);
                string itemMarker = itemTypeMarker ?? marker;
                bool itemMarkerSpecified = itemTypeMarker is null && markerSpecified;
                if (TryCssListStyleType(child, marker, out string? itemCssMarker))
                {
                    itemMarker = itemCssMarker!;
                    itemMarkerSpecified = true;
                }

                bool restart = itemMarker != currentMarker;

                if (reversed)
                {
                    if (!firstItem || explicitValue is not null)
                        restart = true;
                    nextValue = value - 1;
                }
                else
                {
                    if (explicitValue is not null && (!firstItem || value != start))
                        restart = true;
                    nextValue = value + 1;
                }

                if (restart)
                {
                    currentInstance = RestartList(
                        context.Numbering,
                        baseInstance,
                        listLevel,
                        value,
                        definition,
                        itemMarker == marker ? null : itemMarker);
                    currentMarker = itemMarker;
                }

                Inherited currentItem = WithCss(child, context, itemContext) with
                {
                    NumberingId = currentInstance,
                    ListStyleType = itemMarker,
                    ListStyleTypeSpecified = itemMarkerSpecified,
                };
                var itemBlocks = new List<Block>();
                var itemTarget = new BlockTarget(itemBlocks);
                MapBlocks(child, context, itemTarget, currentItem);
                itemTarget.Flush();
                NormalizeListItem(itemBlocks, currentItem, definition);
                foreach (Block block in itemBlocks)
                    target.Add(block);
                firstItem = false;
            }
            else if (child.Name is "ul" or "ol")
            {
                MapList(child, context, target, itemContext);
            }
        }

        if (inherited.NumberingId is null)
            target.Flush();
    }

    private static void NormalizeListItem(List<Block> blocks, Inherited item, NumberingLevel definition)
    {
        int ownerIndex = blocks.FindIndex(block => block is Paragraph paragraph &&
            paragraph.Format.NumberingId == item.NumberingId &&
            paragraph.Format.NumberingLevel == item.ListLevel);
        bool insertOwner = ownerIndex != 0;
        if (insertOwner)
        {
            blocks.Insert(0, new Paragraph
            {
                Format = ParagraphFormat.Default with
                {
                    StyleId = item.StyleId,
                    NumberingId = item.NumberingId,
                    NumberingLevel = item.ListLevel,
                    Alignment = item.Alignment,
                },
            });
        }

        bool keptOwner = false;
        foreach (Paragraph paragraph in blocks.OfType<Paragraph>())
        {
            if (paragraph.Format.NumberingId != item.NumberingId ||
                paragraph.Format.NumberingLevel != item.ListLevel)
            {
                continue;
            }

            if (!keptOwner)
            {
                keptOwner = true;
                continue;
            }

            paragraph.Format = paragraph.Format with
            {
                NumberingId = null,
                NumberingLevel = null,
                StyleId = item.StyleId,
                IndentLeft = definition.ParagraphFormat.IndentLeft,
                IndentFirstLine = null,
                IndentHanging = null,
                IndentLeftCharacters = null,
                IndentFirstLineCharacters = null,
                IndentHangingCharacters = null,
            };
        }
    }

    private static bool TryCssListStyleType(HtmlElement element, string? inherited, out string? marker)
    {
        marker = null;
        bool found = false;
        foreach ((string name, string value) in Css(element))
        {
            if (name != "list-style-type")
                continue;

            if (HtmlCssParser.Identifier(value) is not { } identifier)
                continue;

            string keyword = HtmlCssParser.AsciiLower(identifier);
            if (keyword == "inherit")
            {
                marker = inherited ?? "disc";
                found = true;
            }
            else if (HtmlListStyle.Canonical(keyword) is { } supported)
            {
                marker = supported;
                found = true;
            }
        }

        return found;
    }

    private static string? ListTypeHint(HtmlElement list)
    {
        if (list.Name == "ol")
        {
            return list.Attribute("type") switch
            {
                "1" => "decimal",
                "a" => "lower-latin",
                "A" => "upper-latin",
                "i" => "lower-roman",
                "I" => "upper-roman",
                _ => null,
            };
        }

        return list.Attribute("type") is { } type
            ? HtmlListStyle.Canonical(HtmlCssParser.AsciiLower(type))
            : null;
    }

    private static string? ListItemTypeHint(HtmlElement item)
    {
        return item.Attribute("type") switch
        {
            "1" => "decimal",
            "a" => "lower-latin",
            "A" => "upper-latin",
            "i" => "lower-roman",
            "I" => "upper-roman",
            { } type => HtmlListStyle.Canonical(HtmlCssParser.AsciiLower(type)),
            _ => null,
        };
    }

    private static int RestartList(
        NumberingBuilder numbering,
        NumberingInstance source,
        int level,
        int start,
        NumberingLevel sourceLevel,
        string? marker = null)
    {
        NumberingInstance restarted = numbering.AddInstance(source.AbstractId);
        var levelOverride = new NumberingLevelOverride { Level = level, StartOverride = start };
        if (marker is not null)
        {
            levelOverride.Definition = sourceLevel.Clone();
            HtmlListStyle.Apply(levelOverride.Definition, marker);
        }

        restarted.Overrides.Add(levelOverride);
        return restarted.Id;
    }

    private static int? HtmlInteger(string? value)
    {
        if (value is null)
            return null;

        ReadOnlySpan<char> source = value.AsSpan();
        int index = 0;
        while (index < source.Length && source[index] is ' ' or '\t' or '\n' or '\f' or '\r')
            index++;
        if (index == source.Length)
            return null;

        bool negative = source[index] == '-';
        if (negative || source[index] == '+')
            index++;
        if (index == source.Length || !char.IsAsciiDigit(source[index]))
            return null;

        long parsed = 0;
        long limit = negative ? (long)int.MaxValue + 1 : int.MaxValue;
        while (index < source.Length && char.IsAsciiDigit(source[index]))
        {
            parsed = (parsed * 10) + source[index++] - '0';
            if (parsed > limit)
                return null;
        }

        return negative ? (int)-parsed : (int)parsed;
    }

    private static Table MapTable(HtmlElement table, ImportContext context)
    {
        var result = new Table();
        result.Format = result.Format with { StyleId = context.Document.Styles.GetOrAdd("TableGrid", StyleKind.Table).Id };

        // A rowspan opened above owes continuation cells below; the map says where and how wide.
        var pending = new Dictionary<int, (int RowsLeft, int Span)>();

        foreach (HtmlElement rowElement in Rows(table))
        {
            var row = new TableRow();
            bool header = true;
            int gridColumn = 0;

            void EmitContinuations()
            {
                while (pending.TryGetValue(gridColumn, out (int RowsLeft, int Span) merge))
                {
                    var continuation = new TableCell();
                    continuation.Format = continuation.Format with
                    {
                        VerticalMerge = VerticalMerge.Continue,
                        GridSpan = merge.Span > 1 ? merge.Span : null,
                    };
                    continuation.AddParagraph(string.Empty);
                    row.Cells.Add(continuation);

                    if (merge.RowsLeft <= 1)
                        pending.Remove(gridColumn);
                    else
                        pending[gridColumn] = (merge.RowsLeft - 1, merge.Span);

                    gridColumn += merge.Span;
                }
            }

            foreach (HtmlNode node in rowElement.Children)
            {
                if (node is not HtmlElement cellElement || cellElement.Name is not ("td" or "th"))
                    continue;

                EmitContinuations();

                header &= cellElement.Name == "th";
                int span = ParseCount(cellElement.Attribute("colspan"));
                int rows = ParseCount(cellElement.Attribute("rowspan"));

                var cell = new TableCell();
                if (span > 1)
                    cell.Format = cell.Format with { GridSpan = span };
                if (rows > 1)
                {
                    cell.Format = cell.Format with { VerticalMerge = VerticalMerge.Restart };
                    pending[gridColumn] = (rows - 1, span);
                }

                var cellTarget = new BlockTarget(cell.Blocks);
                Inherited cellContext = new Inherited() with
                {
                    Format = cellElement.Name == "th" ? RunFormat.Default with { Bold = true } : RunFormat.Default,
                    Alignment = AlignmentOf(cellElement),
                };
                MapBlocks(cellElement, context, cellTarget, WithCss(cellElement, context, cellContext));
                cellTarget.Flush();
                if (cell.Blocks.Count == 0)
                    cell.AddParagraph(string.Empty);

                row.Cells.Add(cell);
                gridColumn += span;
            }

            EmitContinuations();
            if (row.Cells.Count == 0)
                continue;

            if (header && rowElement.Children.OfType<HtmlElement>().Any(static c => c.Name is "td" or "th"))
                row.Format = row.Format with { IsHeader = true };

            result.Rows.Add(row);
        }

        if (result.Rows.Count == 0)
        {
            var empty = new TableRow();
            empty.AddCell(string.Empty);
            result.Rows.Add(empty);
        }

        return result;
    }

    private static IEnumerable<HtmlElement> Rows(HtmlElement table)
    {
        foreach (HtmlNode node in table.Children)
        {
            if (node is not HtmlElement child)
                continue;

            if (child.Name == "tr")
            {
                yield return child;
            }
            else if (child.Name is "thead" or "tbody" or "tfoot")
            {
                foreach (HtmlNode inner in child.Children)
                {
                    if (inner is HtmlElement row && row.Name == "tr")
                        yield return row;
                }
            }
        }
    }

    private static int ParseCount(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count is > 0 and <= 1000
            ? count
            : 1;

    /// <summary>Applies the inline CSS the importer understands to what the element passes down.</summary>
    private static Inherited WithCss(HtmlElement element, ImportContext context, Inherited inherited)
    {
        Inherited result = inherited;
        foreach ((string name, string value) in Css(element))
        {
            string keyword = HtmlCssParser.Identifier(value) is { } identifier
                ? HtmlCssParser.AsciiLower(identifier)
                : HtmlCssParser.AsciiLower(value.Trim());
            switch (name)
            {
                case "font-weight" when keyword is "bold" or "bolder" || Numeric(value) >= 600:
                    result = result with { Format = result.Format with { Bold = true } };
                    break;
                case "font-weight" when keyword is "normal" || Numeric(value) is > 0 and < 600:
                    result = result with { Format = result.Format with { Bold = false } };
                    break;
                case "font-style" when keyword is "italic" or "oblique":
                    result = result with { Format = result.Format with { Italic = true } };
                    break;
                case "text-decoration" or "text-decoration-line" when value.Contains("underline", StringComparison.OrdinalIgnoreCase):
                    result = result with { Format = result.Format with { Underline = UnderlineStyle.Single } };
                    break;
                case "text-decoration" or "text-decoration-line" when value.Contains("line-through", StringComparison.OrdinalIgnoreCase):
                    result = result with { Format = result.Format with { Strike = true } };
                    break;
                case "color" when CssColor(value) is { } color:
                    result = result with { Format = result.Format with { Color = color } };
                    break;
                case "background" or "background-color" when CssColor(value) is { } fill:
                    result = result with
                    {
                        Format = result.Format with
                        {
                            Shading = new Shading { Pattern = ShadingPattern.Clear, Fill = fill },
                        },
                    };
                    break;
                case "font-size" when CssLength(value) is { } size:
                    result = result with { Format = result.Format with { Size = size } };
                    break;
                case "font-family":
                {
                    if (HtmlCssParser.FirstFontFamily(value) is { Length: > 0 } family)
                        result = result with { Format = result.Format with { FontAscii = family, FontHighAnsi = family } };
                    break;
                }

                case "font-variant" when value.Contains("small-caps", StringComparison.OrdinalIgnoreCase):
                    result = result with { Format = result.Format with { SmallCaps = true } };
                    break;
                case "text-transform" when keyword == "uppercase":
                    result = result with { Format = result.Format with { Caps = true } };
                    break;
                case "text-align":
                    result = result with
                    {
                        Alignment = keyword switch
                        {
                            "center" => ParagraphAlignment.Center,
                            "right" or "end" => ParagraphAlignment.Right,
                            "justify" => ParagraphAlignment.Justify,
                            "left" or "start" => ParagraphAlignment.Left,
                            _ => result.Alignment,
                        },
                    };
                    break;
                case "list-style-type" when keyword == "inherit":
                    result = result with
                    {
                        ListStyleType = inherited.ListStyleType ?? "disc",
                        ListStyleTypeSpecified = true,
                    };
                    break;
                case "list-style-type" when HtmlListStyle.Canonical(keyword) is { } marker:
                    result = result with { ListStyleType = marker, ListStyleTypeSpecified = true };
                    break;
                default:
                    break;
            }
        }

        if (element.Attribute("align") is { } align)
        {
            result = result with
            {
                Alignment = align.ToLowerInvariant() switch
                {
                    "center" => ParagraphAlignment.Center,
                    "right" => ParagraphAlignment.Right,
                    "justify" => ParagraphAlignment.Justify,
                    "left" => ParagraphAlignment.Left,
                    _ => result.Alignment,
                },
            };
        }

        return result;

        static double Numeric(string value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0;
    }

    private static ParagraphAlignment? AlignmentOf(HtmlElement element) =>
        WithCss(element, null!, new Inherited()).Alignment;

    private static IEnumerable<(string Name, string Value)> Css(HtmlElement element)
    {
        string? style = element.Attribute("style");
        if (style is null)
            yield break;

        foreach (HtmlCssDeclaration declaration in HtmlCssParser.ParseDeclarations(style))
        {
            if (!declaration.Name.StartsWith("mso-", StringComparison.Ordinal))
                yield return (declaration.Name, declaration.Value);
        }
    }

    private static Length? CssLength(string value)
    {
        string trimmed = value.Trim();
        double factor;
        int unitLength;
        if (trimmed.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
        {
            factor = 1;
            unitLength = 2;
        }
        else if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            factor = 0.75;
            unitLength = 2;
        }
        else if (trimmed.EndsWith("rem", StringComparison.OrdinalIgnoreCase))
        {
            factor = 11;
            unitLength = 3;
        }
        else if (trimmed.EndsWith("em", StringComparison.OrdinalIgnoreCase))
        {
            factor = 11;
            unitLength = 2;
        }
        else
            return null;

        string number = trimmed[..^unitLength].TrimEnd();
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && parsed > 0
            ? Length.FromPoints(parsed * factor)
            : null;
    }

    private static WordColor? CssColor(string value)
    {
        string trimmed = value.Trim(' ', '\t', '\n', '\r', '\f');
        if (trimmed.StartsWith('#'))
        {
            string hex = trimmed[1..];
            if (hex.Length == 3)
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);

            return hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)
                ? WordColor.FromRgb(rgb)
                : null;
        }

        if (trimmed.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(')'))
        {
            string[] parts = trimmed[4..^1].Split(',');
            if (parts.Length == 3 &&
                byte.TryParse(parts[0].Trim(), out byte r) &&
                byte.TryParse(parts[1].Trim(), out byte g) &&
                byte.TryParse(parts[2].Trim(), out byte b))
            {
                return WordColor.FromRgb((uint)((r << 16) | (g << 8) | b));
            }

            return null;
        }

        if (HtmlCssParser.Identifier(trimmed) is not { } identifier)
            return null;

        return HtmlCssParser.AsciiLower(identifier) switch
        {
            "black" => WordColor.FromRgb(0x000000),
            "white" => WordColor.FromRgb(0xFFFFFF),
            "red" => WordColor.FromRgb(0xFF0000),
            "green" => WordColor.FromRgb(0x008000),
            "blue" => WordColor.FromRgb(0x0000FF),
            "yellow" => WordColor.FromRgb(0xFFFF00),
            "orange" => WordColor.FromRgb(0xFFA500),
            "purple" => WordColor.FromRgb(0x800080),
            "gray" or "grey" => WordColor.FromRgb(0x808080),
            "silver" => WordColor.FromRgb(0xC0C0C0),
            "maroon" => WordColor.FromRgb(0x800000),
            "navy" => WordColor.FromRgb(0x000080),
            "teal" => WordColor.FromRgb(0x008080),
            "olive" => WordColor.FromRgb(0x808000),
            _ => null,
        };
    }

    /// <summary>A code block collects a trailing newline from the markup; it goes.</summary>
    private static void TrimCodeParagraph(BlockTarget target)
    {
        if (target.HasOpenContent)
        {
            Paragraph paragraph = target.Open(default);
            while (paragraph.TextLength > 0 && paragraph.Text[^1] == '\n')
                paragraph.RemoveText(paragraph.TextLength - 1, 1);
            while (paragraph.TextLength > 0 && paragraph.Text[0] == '\n')
                paragraph.RemoveText(0, 1);
        }
    }

    private static HtmlElement? FindElement(HtmlElement parent, string name)
    {
        foreach (HtmlNode node in parent.Children)
        {
            if (node is not HtmlElement element)
                continue;

            if (element.Name == name)
                return element;

            if (FindElement(element, name) is { } nested)
                return nested;
        }

        return null;
    }

    private static string PlainText(HtmlElement element)
    {
        var text = new System.Text.StringBuilder();
        foreach (HtmlNode node in element.Children)
        {
            if (node is HtmlText t)
                text.Append(t.Value);
            else if (node is HtmlElement child)
                text.Append(PlainText(child));
        }

        return text.ToString();
    }

    private static string NormalizeWhitespace(string text)
    {
        var normalized = new System.Text.StringBuilder(text.Length);
        bool space = false;
        foreach (char c in text)
        {
            if (IsCollapsibleWhitespace(c))
            {
                space = true;
                continue;
            }

            if (space && normalized.Length > 0)
                normalized.Append(' ');

            space = false;
            normalized.Append(c);
        }

        if (space)
            normalized.Append(' ');

        return normalized.ToString();
    }

    private static bool IsCollapsibleWhitespace(char character) =>
        character is '\t' or '\n' or '\f' or '\r' or ' ';
}
