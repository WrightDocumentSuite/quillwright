using System.IO.Compression;
using System.Text;
using System.Xml;
using Quillwright.Diagnostics;
using Quillwright.Html;
using Quillwright.IO;
using Quillwright.Markdown;
using Quillwright.Model;

namespace Quillwright.Tests;

public sealed class DocumentLoadBudgetTests
{
    [Fact]
    public async Task Docx_BoundaryBudgetAcceptsExactPackageMetrics()
    {
        byte[] package = await PackageAsync();
        PackageMetrics metrics = Measure(package);
        DocumentLoadBudget budget = ExactBudget(package, metrics);

        WordDocument document = await WordDocument.LoadAsync(
            new MemoryStream(package),
            new Quillwright.Diagnostics.LoadOptions { Budget = budget },
            TestContext.Current.CancellationToken);

        Assert.Equal("budget", document.GetText());
    }

    [Fact]
    public async Task Docx_EachPackageCeilingFailsWithStableLimitIdentity()
    {
        byte[] package = await PackageAsync();
        PackageMetrics metrics = Measure(package);
        DocumentLoadBudget exact = ExactBudget(package, metrics);
        (string Name, DocumentLoadBudget Budget)[] cases =
        [
            (nameof(DocumentLoadBudget.MaxInputBytes), exact with { MaxInputBytes = package.LongLength - 1 }),
            (nameof(DocumentLoadBudget.MaxPackageParts), exact with { MaxPackageParts = metrics.Parts - 1 }),
            (nameof(DocumentLoadBudget.MaxInflatedBytes), exact with { MaxInflatedBytes = metrics.InflatedBytes - 1 }),
            (nameof(DocumentLoadBudget.MaxPartBytes), exact with { MaxPartBytes = metrics.LargestPartBytes - 1 }),
            (nameof(DocumentLoadBudget.MaxXmlCharactersPerPart),
                exact with { MaxXmlCharactersPerPart = metrics.LargestXmlCharacters - 1 }),
            (nameof(DocumentLoadBudget.MaxXmlNodes), exact with { MaxXmlNodes = metrics.XmlNodes - 1 }),
            (nameof(DocumentLoadBudget.MaxXmlDepth), exact with { MaxXmlDepth = metrics.XmlDepth - 1 }),
        ];

        foreach ((string name, DocumentLoadBudget budget) in cases)
        {
            DocumentLoadLimitException error = await Assert.ThrowsAsync<DocumentLoadLimitException>(async () =>
                await WordDocument.LoadAsync(
                    new MemoryStream(package),
                    new Quillwright.Diagnostics.LoadOptions { Budget = budget },
                    TestContext.Current.CancellationToken));
            Assert.Equal(name, error.LimitName);
            Assert.Contains($"Document load limit '{name}' exceeded", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Docx_NonSeekableInputStopsAtConfiguredByteLimit()
    {
        byte[] package = await PackageAsync();
        await using var input = new NonSeekableReadStream(package);
        var options = new Quillwright.Diagnostics.LoadOptions
        {
            Budget = DocumentLoadBudget.Default with { MaxInputBytes = package.Length - 1 },
        };

        DocumentLoadLimitException error = await Assert.ThrowsAsync<DocumentLoadLimitException>(async () =>
            await WordDocument.LoadAsync(input, options, TestContext.Current.CancellationToken));

        Assert.Equal(nameof(DocumentLoadBudget.MaxInputBytes), error.LimitName);
    }

    [Fact]
    public async Task XmlContentTypesAndMalformedPrefixesCannotBypassPackageCounters()
    {
        byte[] package = await PackageAsync();
        PackageMetrics metrics = Measure(package);

        int customDepth = metrics.XmlDepth + 1;
        string nested = string.Concat(Enumerable.Repeat("<n>", customDepth)) +
                        string.Concat(Enumerable.Repeat("</n>", customDepth));
        byte[] renamedXml = AddCustomXmlPart(package, nested);
        DocumentLoadLimitException depth = await Assert.ThrowsAsync<DocumentLoadLimitException>(async () =>
            await WordDocument.LoadAsync(
                new MemoryStream(renamedXml),
                new LoadOptions
                {
                    Budget = DocumentLoadBudget.Default with { MaxXmlDepth = metrics.XmlDepth },
                },
                TestContext.Current.CancellationToken));

        string malformed = "<root>" + string.Concat(Enumerable.Repeat("<node/>", 8));
        byte[] malformedXml = AddCustomXmlPart(package, malformed);
        DocumentLoadLimitException nodes = await Assert.ThrowsAsync<DocumentLoadLimitException>(async () =>
            await WordDocument.LoadAsync(
                new MemoryStream(malformedXml),
                new LoadOptions
                {
                    Budget = DocumentLoadBudget.Default with { MaxXmlNodes = metrics.XmlNodes + 2 },
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(nameof(DocumentLoadBudget.MaxXmlDepth), depth.LimitName);
        Assert.Equal(nameof(DocumentLoadBudget.MaxXmlNodes), nodes.LimitName);
    }

    [Fact]
    public async Task Docx_MediaBudgetFollowsOpcRoleAtNonstandardTarget()
    {
        byte[] content = Enumerable.Range(0, 37).Select(static value => (byte)value).ToArray();
        byte[] package = AddRelatedParts(
            await PackageAsync(),
            new RelatedPart(
                "resources/preview.dat",
                "image/png",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image",
                content));

        await WordDocument.LoadAsync(
            new MemoryStream(package),
            new LoadOptions
            {
                Budget = DocumentLoadBudget.Default with
                {
                    MaxMediaBytes = content.LongLength,
                    MaxTotalMediaBytes = content.LongLength,
                },
            },
            TestContext.Current.CancellationToken);

        DocumentLoadLimitException item = await Assert.ThrowsAsync<DocumentLoadLimitException>(async () =>
            await WordDocument.LoadAsync(
                new MemoryStream(package),
                new LoadOptions
                {
                    Budget = DocumentLoadBudget.Default with { MaxMediaBytes = content.LongLength - 1 },
                },
                TestContext.Current.CancellationToken));
        DocumentLoadLimitException total = await Assert.ThrowsAsync<DocumentLoadLimitException>(async () =>
            await WordDocument.LoadAsync(
                new MemoryStream(package),
                new LoadOptions
                {
                    Budget = DocumentLoadBudget.Default with
                    {
                        MaxMediaBytes = content.LongLength,
                        MaxTotalMediaBytes = content.LongLength - 1,
                    },
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(nameof(DocumentLoadBudget.MaxMediaBytes), item.LimitName);
        Assert.Equal(nameof(DocumentLoadBudget.MaxTotalMediaBytes), total.LimitName);
    }

    [Fact]
    public async Task Docx_MediaContentTypeRecognizesEscapedNonstandardPartName()
    {
        byte[] content = Enumerable.Repeat((byte)0x6B, 23).ToArray();
        byte[] package = AddRelatedParts(
            await PackageAsync(),
            new RelatedPart(
                "resources/My%20Preview.dat",
                "image/png",
                "https://quillwright.example/relationships/resource",
                content));

        DocumentLoadLimitException error = await Assert.ThrowsAsync<DocumentLoadLimitException>(async () =>
            await WordDocument.LoadAsync(
                new MemoryStream(package),
                new LoadOptions
                {
                    Budget = DocumentLoadBudget.Default with { MaxMediaBytes = content.LongLength - 1 },
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(nameof(DocumentLoadBudget.MaxMediaBytes), error.LimitName);
        Assert.Equal(content.LongLength, error.Observed);
    }

    [Theory]
    [InlineData("http://schemas.openxmlformats.org/officeDocument/2006/relationships/image")]
    [InlineData("http://purl.oclc.org/ooxml/officeDocument/relationships/image")]
    public async Task Docx_MediaBudgetFollowsRelationshipRoleWithoutMediaContentType(string relationshipType)
    {
        byte[] content = Enumerable.Repeat((byte)0x39, 19).ToArray();
        byte[] package = AddRelatedParts(
            await PackageAsync(),
            new RelatedPart(
                "resources/opaque.payload",
                "application/octet-stream",
                relationshipType,
                content));

        DocumentLoadLimitException error = await Assert.ThrowsAsync<DocumentLoadLimitException>(async () =>
            await WordDocument.LoadAsync(
                new MemoryStream(package),
                new LoadOptions
                {
                    Budget = DocumentLoadBudget.Default with { MaxMediaBytes = content.LongLength - 1 },
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(nameof(DocumentLoadBudget.MaxMediaBytes), error.LimitName);
        Assert.Equal(content.LongLength, error.Observed);
    }

    [Fact]
    public async Task Docx_EmbeddedBudgetFollowsOpcRoleAtNonstandardTargets()
    {
        byte[] first = Enumerable.Repeat((byte)0xA5, 29).ToArray();
        byte[] second = Enumerable.Repeat((byte)0x5A, 31).ToArray();
        byte[] package = AddRelatedParts(
            await PackageAsync(),
            new RelatedPart(
                "objects/first.payload",
                "application/vnd.openxmlformats-officedocument.oleObject",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject",
                first),
            new RelatedPart(
                "objects/second.payload",
                "application/vnd.openxmlformats-officedocument.oleObject",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject",
                second));

        await WordDocument.LoadAsync(
            new MemoryStream(package),
            new LoadOptions
            {
                Budget = DocumentLoadBudget.Default with
                {
                    MaxEmbeddedObjectBytes = second.LongLength,
                    MaxEmbeddedObjects = 2,
                },
            },
            TestContext.Current.CancellationToken);

        DocumentLoadLimitException item = await Assert.ThrowsAsync<DocumentLoadLimitException>(async () =>
            await WordDocument.LoadAsync(
                new MemoryStream(package),
                new LoadOptions
                {
                    Budget = DocumentLoadBudget.Default with
                    {
                        MaxEmbeddedObjectBytes = second.LongLength - 1,
                    },
                },
                TestContext.Current.CancellationToken));
        DocumentLoadLimitException count = await Assert.ThrowsAsync<DocumentLoadLimitException>(async () =>
            await WordDocument.LoadAsync(
                new MemoryStream(package),
                new LoadOptions
                {
                    Budget = DocumentLoadBudget.Default with { MaxEmbeddedObjects = 1 },
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(nameof(DocumentLoadBudget.MaxEmbeddedObjectBytes), item.LimitName);
        Assert.Equal(nameof(DocumentLoadBudget.MaxEmbeddedObjects), count.LimitName);
    }

    [Fact]
    public async Task Docx_StrictPackageRelationshipsCountEachPhysicalTargetOnce()
    {
        const string packageRelationship =
            "http://purl.oclc.org/ooxml/officeDocument/relationships/package";
        byte[] content = Enumerable.Repeat((byte)0xC3, 27).ToArray();
        var related = new RelatedPart(
            "objects/linked-package.payload",
            "application/octet-stream",
            packageRelationship,
            content);
        byte[] package = AddRelatedParts(await PackageAsync(), related, related);

        await WordDocument.LoadAsync(
            new MemoryStream(package),
            new LoadOptions
            {
                Budget = DocumentLoadBudget.Default with
                {
                    MaxEmbeddedObjectBytes = content.LongLength,
                    MaxEmbeddedObjects = 1,
                },
            },
            TestContext.Current.CancellationToken);

        DocumentLoadLimitException error = await Assert.ThrowsAsync<DocumentLoadLimitException>(async () =>
            await WordDocument.LoadAsync(
                new MemoryStream(package),
                new LoadOptions
                {
                    Budget = DocumentLoadBudget.Default with
                    {
                        MaxEmbeddedObjectBytes = content.LongLength - 1,
                        MaxEmbeddedObjects = 1,
                    },
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(nameof(DocumentLoadBudget.MaxEmbeddedObjectBytes), error.LimitName);
        Assert.Equal(content.LongLength, error.Observed);
    }

    [Fact]
    public async Task OpcSemanticMetadataScanObservesCancellation()
    {
        const string marker = "cancel-during-semantic-relationship-scan";
        byte[] package = EscapedRelationshipsPackage(marker, leadingAttributeCharacters: 256 * 1024);
        using var cancellation = new CancellationTokenSource();
        await using var input = new MarkerTrackingReadStream(package, marker, cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await using OpcPackage ignored = await OpcPackage.OpenReadAsync(
                input, leaveOpen: true, cancellation.Token);
        });

        Assert.True(input.MarkerReadCount > 0);
    }

    [Fact]
    public async Task OpcPercentEscapedRelationshipsPartIsSemanticallyScannedOnce()
    {
        const string marker = "count-percent-escaped-relationship-scan";
        byte[] package = EscapedRelationshipsPackage(marker);
        await using var input = new MarkerTrackingReadStream(package, marker);

        await using (OpcPackage ignored = await OpcPackage.OpenReadAsync(
                         input, leaveOpen: true, TestContext.Current.CancellationToken))
        {
        }

        Assert.Equal(1, input.MarkerReadCount);
    }

    [Fact]
    public void Html_CharactersLinesNodesAndDepthHaveInclusiveBoundaries()
    {
        const string text = "x\ny";
        HtmlImportResult byText = HtmlImporter.Import(text, new HtmlImportOptions
        {
            Budget = DocumentLoadBudget.Default with { MaxTextCharacters = text.Length, MaxLines = 2 },
        });
        Assert.Equal("x y", byText.Document.GetText());

        AssertLimit(
            nameof(DocumentLoadBudget.MaxTextCharacters),
            () => HtmlImporter.Import(text, new HtmlImportOptions
            {
                Budget = DocumentLoadBudget.Default with { MaxTextCharacters = text.Length - 1 },
            }));
        AssertLimit(
            nameof(DocumentLoadBudget.MaxLines),
            () => HtmlImporter.Import(text, new HtmlImportOptions
            {
                Budget = DocumentLoadBudget.Default with { MaxLines = 1 },
            }));

        HtmlImporter.ImportFragment("x", options: new HtmlImportOptions
        {
            Budget = DocumentLoadBudget.Default with { MaxMarkupNodes = 4 },
        });
        AssertLimit(
            nameof(DocumentLoadBudget.MaxMarkupNodes),
            () => HtmlImporter.ImportFragment("x", options: new HtmlImportOptions
            {
                Budget = DocumentLoadBudget.Default with { MaxMarkupNodes = 3 },
            }));

        HtmlImporter.ImportFragment("<b>x</b>", options: new HtmlImportOptions
        {
            Budget = DocumentLoadBudget.Default with { MaxMarkupDepth = 2 },
        });
        AssertLimit(
            nameof(DocumentLoadBudget.MaxMarkupDepth),
            () => HtmlImporter.ImportFragment("<b>x</b>", options: new HtmlImportOptions
            {
                Budget = DocumentLoadBudget.Default with { MaxMarkupDepth = 1 },
            }));
    }

    [Fact]
    public void Markdown_CharactersLinesNodesAndDepthHaveInclusiveBoundaries()
    {
        const string plain = "plain";
        MarkdownImportResult exact = MarkdownImporter.Import(plain, new MarkdownImportOptions
        {
            Budget = DocumentLoadBudget.Default with
            {
                MaxTextCharacters = plain.Length,
                MaxLines = 1,
                MaxMarkupNodes = 2,
                MaxMarkupDepth = 1,
            },
        });
        Assert.Equal(plain, exact.Document.GetText());

        AssertLimit(
            nameof(DocumentLoadBudget.MaxMarkupNodes),
            () => MarkdownImporter.Import(plain, new MarkdownImportOptions
            {
                Budget = DocumentLoadBudget.Default with { MaxMarkupNodes = 1 },
            }));

        const string nested = "> > quote";
        MarkdownImporter.Import(nested, new MarkdownImportOptions
        {
            Budget = DocumentLoadBudget.Default with { MaxMarkupDepth = 3 },
        });
        AssertLimit(
            nameof(DocumentLoadBudget.MaxMarkupDepth),
            () => MarkdownImporter.Import(nested, new MarkdownImportOptions
            {
                Budget = DocumentLoadBudget.Default with { MaxMarkupDepth = 2 },
            }));
    }

    [Fact]
    public async Task HtmlAndMarkdownFileImportsCheckBytesBeforeDecoding()
    {
        string path = Path.Combine(Path.GetTempPath(), $"quillwright-budget-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(path, "plain", TestContext.Current.CancellationToken);
            long bytes = new FileInfo(path).Length;

            await HtmlImporter.ImportFileAsync(path, new HtmlImportOptions
            {
                Budget = DocumentLoadBudget.Default with { MaxInputBytes = bytes },
            }, TestContext.Current.CancellationToken);
            await MarkdownImporter.ImportFileAsync(path, new MarkdownImportOptions
            {
                Budget = DocumentLoadBudget.Default with { MaxInputBytes = bytes },
            }, TestContext.Current.CancellationToken);

            DocumentLoadLimitException html = await Assert.ThrowsAsync<DocumentLoadLimitException>(async () =>
                await HtmlImporter.ImportFileAsync(path, new HtmlImportOptions
                {
                    Budget = DocumentLoadBudget.Default with { MaxInputBytes = bytes - 1 },
                }, TestContext.Current.CancellationToken));
            DocumentLoadLimitException markdown = await Assert.ThrowsAsync<DocumentLoadLimitException>(async () =>
                await MarkdownImporter.ImportFileAsync(path, new MarkdownImportOptions
                {
                    Budget = DocumentLoadBudget.Default with { MaxInputBytes = bytes - 1 },
                }, TestContext.Current.CancellationToken));

            Assert.Equal(nameof(DocumentLoadBudget.MaxInputBytes), html.LimitName);
            Assert.Equal(nameof(DocumentLoadBudget.MaxInputBytes), markdown.LimitName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HtmlAndMarkdownMediaAreBoundedBeforeBase64Decode()
    {
        const string html = "<img src=\"data:image/png;base64,AQID\" alt=\"x\">";
        const string markdown = "![x](data:image/png;base64,AQID)";
        DocumentLoadBudget exact = DocumentLoadBudget.Default with { MaxMediaBytes = 3, MaxTotalMediaBytes = 3 };

        HtmlImporter.Import(html, new HtmlImportOptions { Budget = exact });
        MarkdownImporter.Import(markdown, new MarkdownImportOptions { Budget = exact });

        AssertLimit(
            nameof(DocumentLoadBudget.MaxMediaBytes),
            () => HtmlImporter.Import(html, new HtmlImportOptions
            {
                Budget = exact with { MaxMediaBytes = 2 },
            }));
        AssertLimit(
            nameof(DocumentLoadBudget.MaxMediaBytes),
            () => MarkdownImporter.Import(markdown, new MarkdownImportOptions
            {
                Budget = exact with { MaxMediaBytes = 2 },
            }));

        DocumentLoadBudget total = exact with { MaxTotalMediaBytes = 5 };
        DocumentLoadLimitException htmlTotal = Assert.Throws<DocumentLoadLimitException>(() =>
            HtmlImporter.Import(html + html, new HtmlImportOptions { Budget = total }));
        DocumentLoadLimitException markdownTotal = Assert.Throws<DocumentLoadLimitException>(() =>
            MarkdownImporter.Import(markdown + "\n\n" + markdown, new MarkdownImportOptions { Budget = total }));

        Assert.Equal(nameof(DocumentLoadBudget.MaxTotalMediaBytes), htmlTotal.LimitName);
        Assert.Equal(6, htmlTotal.Observed);
        Assert.Equal(nameof(DocumentLoadBudget.MaxTotalMediaBytes), markdownTotal.LimitName);
        Assert.Equal(6, markdownTotal.Observed);

        HtmlImporter.Import(
            "<img src=\"data:image/png;base64,!!!!\">" + html,
            new HtmlImportOptions { Budget = exact });
        MarkdownImporter.Import(
            "![bad](data:image/png;base64,!!!!)\n\n" + markdown,
            new MarkdownImportOptions { Budget = exact });
    }

    private static void AssertLimit(string name, Action action)
    {
        DocumentLoadLimitException error = Assert.Throws<DocumentLoadLimitException>(action);
        Assert.Equal(name, error.LimitName);
    }

    private static async Task<byte[]> PackageAsync()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("budget");
        using var stream = new MemoryStream();
        await document.SaveAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        return stream.ToArray();
    }

    private static byte[] AddCustomXmlPart(byte[] package, string xml)
    {
        using var output = new MemoryStream();
        using (var destination = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        using (var source = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        {
            foreach (ZipArchiveEntry entry in source.Entries)
            {
                ZipArchiveEntry copy = destination.CreateEntry(entry.FullName, CompressionLevel.SmallestSize);
                using Stream target = copy.Open();
                using Stream input = entry.Open();
                if (!entry.FullName.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                {
                    input.CopyTo(target);
                    continue;
                }

                using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                string types = reader.ReadToEnd().Replace(
                    "</Types>",
                    "<Override PartName=\"/custom/resource.bin\" ContentType=\"application/vnd.quillwright.test+xml\"/></Types>",
                    StringComparison.Ordinal);
                using var typeWriter = new StreamWriter(target, new UTF8Encoding(false), leaveOpen: true);
                typeWriter.Write(types);
            }

            ZipArchiveEntry custom = destination.CreateEntry("custom/resource.bin", CompressionLevel.SmallestSize);
            using var customWriter = new StreamWriter(custom.Open(), new UTF8Encoding(false));
            customWriter.Write(xml);
        }

        return output.ToArray();
    }

    private static byte[] AddRelatedParts(byte[] package, params RelatedPart[] parts)
    {
        RelatedPart[] physicalParts = parts
            .DistinctBy(static part => part.EntryName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        using var output = new MemoryStream();
        using (var destination = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        using (var source = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        {
            foreach (ZipArchiveEntry entry in source.Entries)
            {
                ZipArchiveEntry copy = destination.CreateEntry(entry.FullName, CompressionLevel.SmallestSize);
                using Stream target = copy.Open();
                using Stream input = entry.Open();
                if (entry.FullName.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                {
                    using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    string declarations = string.Concat(physicalParts.Select(static part =>
                        $"<Override PartName=\"/{part.EntryName}\" ContentType=\"{part.ContentType}\"/>"));
                    string types = reader.ReadToEnd().Replace(
                        "</Types>", declarations + "</Types>", StringComparison.Ordinal);
                    using var writer = new StreamWriter(target, new UTF8Encoding(false), leaveOpen: true);
                    writer.Write(types);
                    continue;
                }

                if (entry.FullName.Equals("word/_rels/document.xml.rels", StringComparison.OrdinalIgnoreCase))
                {
                    using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    string declarations = string.Concat(parts.Select((part, index) =>
                        $"<Relationship Id=\"rIdBudget{index + 1}\" Type=\"{part.RelationshipType}\" Target=\"../{part.EntryName}\"/>"));
                    string relationships = reader.ReadToEnd().Replace(
                        "</Relationships>", declarations + "</Relationships>", StringComparison.Ordinal);
                    using var writer = new StreamWriter(target, new UTF8Encoding(false), leaveOpen: true);
                    writer.Write(relationships);
                    continue;
                }

                input.CopyTo(target);
            }

            foreach (RelatedPart part in physicalParts)
            {
                ZipArchiveEntry added = destination.CreateEntry(part.EntryName, CompressionLevel.SmallestSize);
                using Stream content = added.Open();
                content.Write(part.Content);
            }
        }

        return output.ToArray();
    }

    private static byte[] EscapedRelationshipsPackage(string marker, int leadingAttributeCharacters = 0)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry relationships = archive.CreateEntry(
                "word/_rels/My%20Document.xml.rels", CompressionLevel.NoCompression);
            using (var writer = new StreamWriter(relationships.Open(), new UTF8Encoding(false)))
            {
                writer.Write(
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    $"<Relationship Id=\"{new string('x', leadingAttributeCharacters)}{marker}\" " +
                    "Type=\"https://quillwright.example/relationships/resource\" " +
                    "Target=\"../missing.payload\"/>" +
                    "</Relationships>");
            }

            // Keep the marker outside the EOCD look-behind window. The tracking stream then
            // observes only reads of the physical relationships payload, not ZIP discovery.
            ZipArchiveEntry padding = archive.CreateEntry("padding.bin", CompressionLevel.NoCompression);
            using Stream paddingContent = padding.Open();
            paddingContent.Write(new byte[128 * 1024]);
        }

        return output.ToArray();
    }

    private static DocumentLoadBudget ExactBudget(byte[] package, PackageMetrics metrics) =>
        DocumentLoadBudget.Default with
        {
            MaxInputBytes = package.LongLength,
            MaxPackageParts = metrics.Parts,
            MaxInflatedBytes = metrics.InflatedBytes,
            MaxPartBytes = metrics.LargestPartBytes,
            MaxXmlCharactersPerPart = metrics.LargestXmlCharacters,
            MaxXmlNodes = metrics.XmlNodes,
            MaxXmlDepth = metrics.XmlDepth,
        };

    private static PackageMetrics Measure(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        long inflated = 0;
        long largestPart = 0;
        long xmlNodes = 0;
        long largestXmlCharacters = 0;
        int xmlDepth = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            inflated += entry.Length;
            largestPart = Math.Max(largestPart, entry.Length);
            if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                !entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var buffered = new MemoryStream();
            using (Stream content = entry.Open())
                content.CopyTo(buffered);
            byte[] xml = buffered.ToArray();
            using XmlReader reader = XmlReader.Create(
                new MemoryStream(xml, writable: false), Quillwright.Xml.XmlDefaults.ReaderSettings);
            while (reader.Read())
            {
                xmlNodes++;
                xmlDepth = Math.Max(xmlDepth, reader.Depth + 1);
            }

            largestXmlCharacters = Math.Max(largestXmlCharacters, MinimumXmlCharacterLimit(xml));
        }

        return new PackageMetrics(
            archive.Entries.Count, inflated, largestPart, xmlNodes, largestXmlCharacters, xmlDepth);
    }

    private static long MinimumXmlCharacterLimit(byte[] xml)
    {
        long low = 1;
        long high = Math.Max(1, xml.LongLength * 2);
        while (low < high)
        {
            long middle = low + ((high - low) / 2);
            if (FitsXmlCharacterLimit(xml, middle))
                high = middle;
            else
                low = middle + 1;
        }

        return low;
    }

    private static bool FitsXmlCharacterLimit(byte[] xml, long limit)
    {
        try
        {
            DocumentLoadBudget budget = DocumentLoadBudget.Default with { MaxXmlCharactersPerPart = limit };
            using XmlReader reader = XmlReader.Create(
                new MemoryStream(xml, writable: false), Quillwright.Xml.XmlDefaults.ForBudget(budget));
            while (reader.Read()) { }
            return true;
        }
        catch (XmlException exception)
            when (exception.Message.Contains(
                nameof(XmlReaderSettings.MaxCharactersInDocument), StringComparison.Ordinal))
        {
            return false;
        }
    }

    private sealed record PackageMetrics(
        int Parts,
        long InflatedBytes,
        long LargestPartBytes,
        long XmlNodes,
        long LargestXmlCharacters,
        int XmlDepth);

    private sealed record RelatedPart(
        string EntryName,
        string ContentType,
        string RelationshipType,
        byte[] Content);

    private sealed class NonSeekableReadStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class MarkerTrackingReadStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly long _markerOffset;
        private readonly CancellationTokenSource? _cancellation;

        public MarkerTrackingReadStream(
            byte[] content,
            string marker,
            CancellationTokenSource? cancellation = null)
        {
            _inner = new MemoryStream(content, writable: false);
            _markerOffset = content.AsSpan().IndexOf(Encoding.UTF8.GetBytes(marker));
            Assert.True(_markerOffset >= 0, "The marker must be stored verbatim in the test package.");
            _cancellation = cancellation;
        }

        public int MarkerReadCount { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long start = _inner.Position;
            int read = _inner.Read(buffer, offset, count);
            Observe(start, read);
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            long start = _inner.Position;
            int read = _inner.Read(buffer);
            Observe(start, read);
            return read;
        }

        public override int ReadByte()
        {
            long start = _inner.Position;
            int value = _inner.ReadByte();
            Observe(start, value < 0 ? 0 : 1);
            return value;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            long start = _inner.Position;
            int read = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
            Observe(start, read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            long start = _inner.Position;
            int read = await _inner.ReadAsync(buffer, cancellationToken);
            Observe(start, read);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }

        private void Observe(long start, int read)
        {
            if (read <= 0 || start > _markerOffset || start + read <= _markerOffset)
                return;

            MarkerReadCount++;
            _cancellation?.Cancel();
        }
    }
}
