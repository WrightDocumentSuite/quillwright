using System.Text;
using Quillwright.Html;
using Quillwright.Model;
using Quillwright.Rendering;
using Quillwright.Styles;

namespace Quillwright.Tests;

public class HtmlListConformanceTests
{
    [Fact]
    public void NumberingResolver_SnapshotsFirstMatchingIdsForConstantTimeReuse()
    {
        var firstLevel = new NumberingLevel { Level = 0, Start = 41 };
        var firstDefinition = new AbstractNumbering { Id = 7 };
        firstDefinition.Levels.Add(firstLevel);
        var duplicateDefinition = new AbstractNumbering { Id = 7 };
        duplicateDefinition.Levels.Add(new NumberingLevel { Level = 0, Start = 99 });
        var firstInstance = new NumberingInstance { Id = 13, AbstractId = 7 };
        var duplicateInstance = new NumberingInstance { Id = 13, AbstractId = 70 };
        var numbering = new NumberingDefinitions();
        numbering.Definitions.Add(firstDefinition);
        numbering.Definitions.Add(duplicateDefinition);
        numbering.Instances.Add(firstInstance);
        numbering.Instances.Add(duplicateInstance);

        var resolver = new NumberingResolver(numbering);
        numbering.Definitions.Clear();
        numbering.Instances.Clear();

        Assert.Same(firstInstance, resolver.FindInstance(13));
        Assert.Same(firstDefinition, resolver.ResolveDefinition(13));
        Assert.Same(firstLevel, resolver.ResolveLevel(13, 0));
    }

    [Fact]
    public void NumberingResolver_SnapshotFollowsNumberingStyleLinks()
    {
        WordDocument document = WordDocument.Create();
        int realId = document.Numbering.AddNumberedList();
        NumberingLevel realLevel = Assert.IsType<NumberingLevel>(document.Numbering.ResolveLevel(realId, 0));
        document.Styles.Add(new Style("IndexedListStyle", StyleKind.Numbering)
        {
            ParagraphFormat = ParagraphFormat.Default with { NumberingId = realId },
        });
        var deferring = new AbstractNumbering { Id = 71, NumberingStyleLink = "IndexedListStyle" };
        var instance = new NumberingInstance { Id = 73, AbstractId = deferring.Id };
        document.Numbering.Definitions.Add(deferring);
        document.Numbering.Instances.Add(instance);

        var resolver = new NumberingResolver(document.Numbering);

        Assert.Same(realLevel, resolver.ResolveLevel(instance.Id, 0));
        Assert.Same(document.Numbering.ResolveDefinition(realId), resolver.ResolveDefinition(instance.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestartHeavyOrderedList_PreservesEveryOrdinalThroughDocxAndHtml(bool reversed)
    {
        const int Count = 384;
        int[] expected = reversed
            ? [.. Enumerable.Range(1, Count).Reverse()]
            : [.. Enumerable.Range(0, Count).Select(static index => 1000 + (index * 2))];
        WordDocument document = HtmlImporter.Import(RestartHeavyList(reversed, Count)).Document;

        Assert.Equal(Count, document.Numbering.Instances.Count);
        Assert.Equal(Count - 1, document.Numbering.Instances.Count(static instance =>
            instance.Overrides.Any(static level => level.StartOverride is not null)));
        Assert.Equal(expected, NumberedValues(document));

        using MemoryStream package = await DocumentFixture.SaveAsync(document);
        WordDocument reloaded = await WordDocument.LoadAsync(
            package,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(expected, NumberedValues(reloaded));

        string exported = reloaded.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;
        WordDocument reimported = HtmlImporter.Import(exported).Document;
        Assert.Equal(expected, NumberedValues(reimported));
    }

    [Fact]
    public void InterleavedNestedListsAndRestarts_AllocateUniqueIdsWithoutRescanning()
    {
        const int Count = 192;
        var source = new StringBuilder(Count * 64).Append("<ol start='1000'>");
        for (int index = 0; index < Count; index++)
        {
            source.Append("<li value='").Append(1000 + (index * 2)).Append("'>outer-")
                .Append(index).Append("<ul><li>inner-").Append(index).Append("</li></ul></li>");
        }

        WordDocument document = HtmlImporter.Import(source.Append("</ol>").ToString()).Document;
        int[] ids = [.. document.Numbering.Instances.Select(static instance => instance.Id)];

        Assert.Equal(Count * 2, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(ids, static id => Assert.True(id > 0));

        var counter = new NumberingCounter(document.Numbering);
        int[] outerValues = [.. document.Paragraphs
            .Select(paragraph => (paragraph.Text, Label: counter.Next(paragraph.Format)))
            .Where(static item => item.Text.StartsWith("outer-", StringComparison.Ordinal))
            .Select(static item => item.Label!.Value.Value)];
        Assert.Equal(
            Enumerable.Range(0, Count).Select(static index => 1000 + (index * 2)),
            outerValues);
    }

    [Fact]
    public void MultiParagraphItem_KeepsOneMarkerAndOneHtmlListItem()
    {
        WordDocument document = HtmlImporter.Import(
            "<ol><li><p>alpha</p><p>beta</p></li><li>gamma</li></ol>").Document;
        Paragraph[] paragraphs = [.. document.Paragraphs];

        Assert.Equal(3, paragraphs.Length);
        Assert.NotNull(paragraphs[0].Format.NumberingId);
        Assert.Null(paragraphs[1].Format.NumberingId);
        Assert.Equal("ListParagraph", paragraphs[1].Format.StyleId);
        Assert.NotNull(paragraphs[1].Format.IndentLeft);
        Assert.NotNull(paragraphs[2].Format.NumberingId);

        HtmlElement list = OnlyTopLevelList(ExportBody(document), "ol");
        HtmlElement[] items = DirectChildren(list, "li");
        Assert.Equal(2, items.Length);
        Assert.Equal("alphabetagamma", CompactText(list));
        Assert.Contains(items[0].Children.OfType<HtmlElement>(), static child => child.Is("p"));
    }

    [Fact]
    public async Task ListContinuation_SurvivesDocxSaveAndLoad()
    {
        WordDocument imported = HtmlImporter.Import(
            "<ol><li><p>alpha</p><p>beta</p></li><li>gamma</li></ol>").Document;
        using MemoryStream package = await DocumentFixture.SaveAsync(imported);

        WordDocument reloaded = await WordDocument.LoadAsync(
            package,
            cancellationToken: TestContext.Current.CancellationToken);
        Paragraph[] paragraphs = [.. reloaded.Paragraphs];

        Assert.Equal(3, paragraphs.Length);
        Assert.NotNull(paragraphs[0].Format.NumberingId);
        Assert.Null(paragraphs[1].Format.NumberingId);
        Assert.Equal("ListParagraph", paragraphs[1].Format.StyleId);
        Assert.NotNull(paragraphs[1].Format.IndentLeft);
        Assert.Equal(2, DirectChildren(OnlyTopLevelList(ExportBody(reloaded), "ol"), "li").Length);
    }

    [Fact]
    public void TextAfterNestedList_RemainsInTheOwningItem()
    {
        WordDocument document = HtmlImporter.Import(
            "<ul><li>before<ul><li>child</li></ul>after</li><li>end</li></ul>").Document;

        HtmlElement list = OnlyTopLevelList(ExportBody(document), "ul");
        HtmlElement[] items = DirectChildren(list, "li");
        Assert.Equal(2, items.Length);
        Assert.Contains(items[0].Children.OfType<HtmlElement>(), static child => child.Is("ul"));
        Assert.Contains(items[0].Children.OfType<HtmlElement>(), static child => child.Is("p") && PlainText(child) == "after");
        Assert.Equal("beforechildafter", CompactText(items[0]));
    }

    [Fact]
    public void EmptyListItem_IsPreservedAndConsumesAnOrdinal()
    {
        WordDocument document = HtmlImporter.Import("<ol><li></li><li>x</li></ol>").Document;
        Paragraph[] paragraphs = [.. document.Paragraphs];

        Assert.Equal(2, paragraphs.Length);
        Assert.True(paragraphs[0].IsEmpty);
        Assert.NotNull(paragraphs[0].Format.NumberingId);
        Assert.Equal([1, 2], NumberedValues(document));

        HtmlElement list = OnlyTopLevelList(ExportBody(document), "ol");
        HtmlElement[] items = DirectChildren(list, "li");
        Assert.Equal(2, items.Length);
        Assert.Equal(string.Empty, PlainText(items[0]));
        Assert.Equal("x", PlainText(items[1]));
    }

    [Fact]
    public void SiblingNestedLists_StayInsideTheirOwningItem()
    {
        WordDocument document = HtmlImporter.Import(
            "<ul><li>parent<ul><li>a</li></ul><ol><li>b</li></ol></li><li>end</li></ul>").Document;
        HtmlElement body = ExportBody(document);
        HtmlElement outer = OnlyTopLevelList(body, "ul");
        HtmlElement owner = DirectChildren(outer, "li")[0];

        Assert.Equal(["ul", "ol"], owner.Children.OfType<HtmlElement>()
            .Where(static child => child.Is("ul") || child.Is("ol"))
            .Select(static child => child.Name)
            .ToArray());
        Assert.DoesNotContain(body.Children.OfType<HtmlElement>(), static child => child.Is("ol"));
    }

    [Theory]
    [InlineData("<ol style='list-style-type:decimal-leading-zero'><li>x</li></ol>", "ol", "decimal-leading-zero")]
    [InlineData("<ol style='list-style-type:none'><li>x</li></ol>", "ol", "none")]
    [InlineData("<ul style='list-style-type:circle'><li>x</li></ul>", "ul", "circle")]
    [InlineData("<ul style='list-style-type:square'><li>x</li></ul>", "ul", "square")]
    public void Css2Marker_RoundTrips(string source, string tag, string marker)
    {
        WordDocument document = HtmlImporter.Import(source).Document;

        HtmlElement list = OnlyTopLevelList(ExportBody(document), tag);
        Assert.Contains($"list-style-type:{marker}", list.Attribute("style"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<div style='list-style-type:square'><ul><li>x</li></ul></div>", "square")]
    [InlineData("<div style='list-style-type:upper-roman'><ol style='list-style-type:inherit'><li>x</li></ol></div>", "upper-roman")]
    [InlineData("<ol style='list-style-type:upper\\2d roman'><li>x</li></ol>", "upper-roman")]
    [InlineData("<ol type='A' style='list-style-type:lower-roman!important;list-style-type:decimal'><li>x</li></ol>", "lower-roman")]
    public void Css2Marker_InheritsAndDecodesIdentifiers(string source, string marker)
    {
        WordDocument document = HtmlImporter.Import(source).Document;

        HtmlElement list = ExportBody(document).Children.OfType<HtmlElement>()
            .SelectMany(SelfAndDescendants)
            .First(static element => element.Is("ul") || element.Is("ol"));
        Assert.Equal(marker, EffectiveMarker(list));
    }

    [Fact]
    public void ListItemCssMarker_OverridesTypeAndThenRevertsForTheNextItem()
    {
        WordDocument document = HtmlImporter.Import(
            "<ol><li type='A' style='list-style-type:lower-roman'>one</li><li>two</li></ol>").Document;
        HtmlElement list = OnlyTopLevelList(ExportBody(document), "ol");
        HtmlElement[] items = DirectChildren(list, "li");

        Assert.Contains("list-style-type:lower-roman", items[0].Attribute("style"), StringComparison.Ordinal);
        Assert.Null(items[1].Attribute("style"));
        Assert.Equal([1, 2], NumberedValues(document));
    }

    [Theory]
    [InlineData("<ol start='5junk'><li>x</li></ol>", 5)]
    [InlineData("<ol start='  +7tail'><li>x</li></ol>", 7)]
    [InlineData("<ol start='\u00a05'><li>x</li></ol>", 1)]
    public void Start_UsesWhatwgIntegerParsing(string source, int expected)
    {
        WordDocument document = HtmlImporter.Import(source).Document;

        Assert.Equal([expected], NumberedValues(document));
    }

    [Fact]
    public void ListItemValue_UsesWhatwgIntegerPrefix()
    {
        WordDocument document = HtmlImporter.Import(
            "<ol><li>one</li><li value='7tail'>seven</li><li>eight</li></ol>").Document;

        Assert.Equal([1, 7, 8], NumberedValues(document));
    }

    [Theory]
    [InlineData("<ul type='none'><li>x</li></ul>", "ul", "none")]
    [InlineData("<ol><li type='A'>x</li></ol>", "li", "upper-latin")]
    public void LegacyTypeHints_MapToListStyle(string source, string target, string marker)
    {
        WordDocument document = HtmlImporter.Import(source).Document;
        HtmlElement body = ExportBody(document);
        HtmlElement element = body.Children.OfType<HtmlElement>()
            .SelectMany(SelfAndDescendants)
            .First(candidate => candidate.Is(target));

        Assert.Contains($"list-style-type:{marker}", element.Attribute("style"), StringComparison.Ordinal);
    }

    private static int[] NumberedValues(WordDocument document)
    {
        var counter = new NumberingCounter(document.Numbering);
        return [.. document.Paragraphs
            .Where(static paragraph => paragraph.Format.NumberingId is not null)
            .Select(paragraph => counter.Next(paragraph.Format)!.Value.Value)];
    }

    private static string RestartHeavyList(bool reversed, int count)
    {
        var html = new StringBuilder(count * 32);
        html.Append(reversed ? "<ol reversed>" : "<ol start='1000'>");
        for (int index = 0; index < count; index++)
        {
            html.Append("<li");
            if (!reversed)
                html.Append(" value='").Append(1000 + (index * 2)).Append('\'');
            html.Append('>').Append(index).Append("</li>");
        }

        return html.Append("</ol>").ToString();
    }

    private static HtmlElement ExportBody(WordDocument document)
    {
        string html = document.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;
        return SelfAndDescendants(HtmlParser.Parse(html)).Single(static element => element.Is("body"));
    }

    private static HtmlElement OnlyTopLevelList(HtmlElement body, string name) =>
        body.Children.OfType<HtmlElement>().Single(element => element.Is(name));

    private static HtmlElement[] DirectChildren(HtmlElement parent, string name) =>
        [.. parent.Children.OfType<HtmlElement>().Where(element => element.Is(name))];

    private static IEnumerable<HtmlElement> SelfAndDescendants(HtmlElement parent)
    {
        yield return parent;
        foreach (HtmlElement child in parent.Children.OfType<HtmlElement>())
        {
            foreach (HtmlElement descendant in SelfAndDescendants(child))
                yield return descendant;
        }
    }

    private static string PlainText(HtmlElement parent) => string.Concat(parent.Children.Select(node => node switch
    {
        HtmlText text => text.Value,
        HtmlElement element => PlainText(element),
        _ => string.Empty,
    }));

    private static string CompactText(HtmlElement parent) =>
        string.Concat(PlainText(parent).Where(static character => !char.IsWhiteSpace(character)));

    private static string EffectiveMarker(HtmlElement list)
    {
        string? style = list.Attribute("style");
        const string Prefix = "list-style-type:";
        if (style is not null && style.IndexOf(Prefix, StringComparison.Ordinal) is int at and >= 0)
            return style[(at + Prefix.Length)..].Split(';')[0];

        return list.Attribute("type") switch
        {
            "i" => "lower-roman",
            "I" => "upper-roman",
            "a" => "lower-latin",
            "A" => "upper-latin",
            _ => list.Is("ol") ? "decimal" : "disc",
        };
    }
}
