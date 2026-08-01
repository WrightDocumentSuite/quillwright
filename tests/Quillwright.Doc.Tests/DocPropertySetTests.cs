using Quillwright.IO;
using Quillwright.Model;

namespace Quillwright.Doc.Tests;

/// <summary>
/// The property sets a compound file carries beside the document: the summary, and the
/// user-defined half of the document summary where custom properties live.
/// </summary>
public class DocPropertySetTests
{
    [Fact]
    public void CustomProperties_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("body"));
        document.CustomProperties.Set("Reviewer", "A. Editor");
        document.CustomProperties.Set("Revision", 4L);
        document.CustomProperties.Set("Approved", true);

        WordDocument reopened = DocReader.Load(DocWriter.Save(document));

        Assert.Equal("A. Editor", reopened.CustomProperties["Reviewer"]?.Value.AsText());
        Assert.Equal(4L, reopened.CustomProperties["Revision"]?.Value.AsInteger());
        Assert.True(reopened.CustomProperties["Approved"]?.Value.AsBoolean());
    }

    [Fact]
    public void TheCompanyAndTheCategory_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("body"));
        document.Properties.Title = "Contract";
        document.Properties.Category = "Legal";
        document.ApplicationProperties.Company = "Acme";
        document.ApplicationProperties.Manager = "R. Manager";

        WordDocument reopened = DocReader.Load(DocWriter.Save(document));

        Assert.Equal("Contract", reopened.Properties.Title);
        Assert.Equal("Legal", reopened.Properties.Category);
        Assert.Equal("Acme", reopened.ApplicationProperties.Company);
        Assert.Equal("R. Manager", reopened.ApplicationProperties.Manager);
    }

    /// <summary>
    /// A name in the dictionary is counted in characters, and whether a character is one byte
    /// or two is what the set's code page says — the two must agree or the names come back as
    /// text riddled with nulls.
    /// </summary>
    [Fact]
    public void ANameInTheDictionary_IsReadThroughTheSetsOwnCodePage()
    {
        var section = new PropertySetSection(PropertySetStream.UserDefinedFormat);
        section.Names[2] = "Département";
        section.Values[2] = PropertyValue.FromText("Ventes");

        List<PropertySetSection> read = PropertySetStream.Read(PropertySetStream.Build(section));

        Assert.Equal("Département", read.Single().Names[2]);
        Assert.Equal("Ventes", read.Single().Values[2].AsText());
        Assert.DoesNotContain('\0', read.Single().Names[2]);
    }

    /// <summary>Two sets in one stream is the historical shape of the document summary.</summary>
    [Fact]
    public void AStreamOfTwoSets_ReadsBackAsTwo()
    {
        var summary = new PropertySetSection(PropertySetStream.DocumentSummaryFormat);
        summary.Values[0x0F] = PropertyValue.FromText("Acme");
        var user = new PropertySetSection(PropertySetStream.UserDefinedFormat);
        user.Names[2] = "Owner";
        user.Values[2] = PropertyValue.FromText("Finance");

        List<PropertySetSection> read = PropertySetStream.Read(PropertySetStream.Build(summary, user));

        Assert.Equal(2, read.Count);
        Assert.Equal("Acme", read[0].Values[0x0F].AsText());
        Assert.Equal("Finance", read[1].Values[2].AsText());
    }

    /// <summary>
    /// Every typed value in the corpus has to come back as the type it went in as, or a
    /// property that is a number would read as text and compare unequal.
    /// </summary>
    [Theory]
    [InlineData(PropertyValueKind.Text)]
    [InlineData(PropertyValueKind.Integer)]
    [InlineData(PropertyValueKind.Real)]
    [InlineData(PropertyValueKind.Boolean)]
    [InlineData(PropertyValueKind.DateTime)]
    public void EveryValueType_KeepsItsTypeThroughTheRoundTrip(PropertyValueKind kind)
    {
        var section = new PropertySetSection(PropertySetStream.UserDefinedFormat);
        section.Values[2] = kind switch
        {
            PropertyValueKind.Integer => PropertyValue.FromInteger(-17),
            PropertyValueKind.Real => PropertyValue.FromReal(2.25),
            PropertyValueKind.Boolean => PropertyValue.FromBoolean(true),
            PropertyValueKind.DateTime => PropertyValue.FromDateTime(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero)),
            _ => PropertyValue.FromText("text"),
        };

        PropertyValue read = PropertySetStream.Read(PropertySetStream.Build(section)).Single().Values[2];

        Assert.Equal(kind, read.Kind);
        Assert.Equal(section.Values[2], read);
    }

    /// <summary>The corpus is what proves the reader copes with sets Word itself wrote.</summary>
    [Fact]
    public void CustomProperties_AreFoundInTheCorpus()
    {
        string root = ReferenceCorpus.Telerik;
        Assert.SkipUnless(Directory.Exists(root), ReferenceCorpus.Absent);

        List<CustomProperty> found = [];
        foreach (string path in Directory.EnumerateFiles(root, "*.doc", SearchOption.AllDirectories))
        {
            if (new FileInfo(path).Length is not (> 0 and < 8 * 1024 * 1024))
                continue;

            try
            {
                found.AddRange(DocReader.Load(File.ReadAllBytes(path)).CustomProperties);
            }
            catch (Exception error) when (RefusedByDesign.Matches(error))
            {
                // Encrypted and pre-Word 97 files are refused rather than read.
            }
        }

        Assert.NotEmpty(found);
        Assert.All(found, static property => Assert.DoesNotContain('\0', property.Name));
    }
}
