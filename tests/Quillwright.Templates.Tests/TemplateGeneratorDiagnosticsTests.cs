using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quillwright.Model;
using Quillwright.Templates.Generator;

namespace Quillwright.Templates.Tests;

public sealed class TemplateGeneratorDiagnosticsTests
{
    private static (Diagnostic[] Diagnostics, int GeneratedSources) RunGenerator(string source)
    {
        (GeneratorDriverRunResult result, _) = Compile(source);
        return ([.. result.Diagnostics], result.GeneratedTrees.Length);
    }

    private static (GeneratorDriverRunResult Result, Compilation Output) Compile(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "TemplateGeneratorTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(WordTemplateAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ImageData).Assembly.Location),
                MetadataReference.CreateFromFile(System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                    "System.Runtime.dll")),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new TemplateGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        GeneratorDriverRunResult result = driver.GetRunResult();
        return (result, output);
    }

    [Fact]
    public void LongMetadataNames_HaveBoundedDeterministicFileNames()
    {
        string longName = new('N', 300);
        string source = $$"""
            using Quillwright.Templates;
            namespace {{longName}}
            {
                public partial class {{longName}}<T>
                {
                    [WordTemplate]
                    public partial class @class<U> { }
                }
            }
            """;

        (GeneratorDriverRunResult first, Compilation output) = Compile(source);
        (GeneratorDriverRunResult second, _) = Compile(source);
        GeneratedSourceResult generated = Assert.Single(Assert.Single(first.Results).GeneratedSources);
        Assert.Empty(first.Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(TestContext.Current.CancellationToken), d => d.Severity == DiagnosticSeverity.Error);
        Assert.InRange(generated.HintName.Length, 1, 100);
        Assert.EndsWith(".Template.g.cs", generated.HintName, StringComparison.Ordinal);
        Assert.Equal(generated.HintName, Assert.Single(Assert.Single(second.Results).GeneratedSources).HintName);
    }

    [Fact]
    public void SimilarMetadataNames_ProduceDistinctFileNamesAndCompile()
    {
        const string source = """
            using Quillwright.Templates;
            namespace A.B { [WordTemplate] public partial class C { } }
            namespace A_B { [WordTemplate] public partial class C { } }
            namespace Containers
            {
                public partial class B { [WordTemplate] public partial class C { } }
                public partial class B<T> { [WordTemplate] public partial class C { } }
                public partial class B<T, U> { [WordTemplate] public partial class C { } }
            }
            namespace @event { [WordTemplate] public partial class @class { } }
            """;

        (GeneratorDriverRunResult result, Compilation output) = Compile(source);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(TestContext.Current.CancellationToken), d => d.Severity == DiagnosticSeverity.Error);
        var generated = Assert.Single(result.Results).GeneratedSources;
        Assert.Equal(6, generated.Length);
        Assert.Equal(6, generated.Select(s => s.HintName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void NonPartialType_ReportsQwt001WithSourceLocation()
    {
        (Diagnostic[] diagnostics, int generated) = RunGenerator(
            """
            using Quillwright.Templates;

            [WordTemplate]
            public record MissingPartial(string Name);
            """);

        Diagnostic diag = Assert.Single(diagnostics, d => d.Id == "QWT001");
        Assert.NotEqual(Location.None, diag.Location);
        Assert.Equal(0, generated);
    }

    [Fact]
    public void NonPartialOuterContainer_ReportsQwt001()
    {
        (Diagnostic[] diagnostics, int generated) = RunGenerator(
            """
            using Quillwright.Templates;

            public class OuterNotPartial
            {
                [WordTemplate]
                public partial record Inner(string Name);
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "QWT001");
        Assert.Equal(0, generated);
    }

    [Fact]
    public void AbstractTemplateModel_ReportsQwt002()
    {
        (Diagnostic[] diagnostics, int generated) = RunGenerator(
            """
            using Quillwright.Templates;

            [WordTemplate]
            public abstract partial class AbstractModel
            {
                public string Name { get; set; } = "";
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "QWT002");
        Assert.Equal(0, generated);
    }

    [Fact]
    public void TemplateRowsWithNonTemplateElement_ReportsQwt003()
    {
        (Diagnostic[] diagnostics, int generated) = RunGenerator(
            """
            using System.Collections.Generic;
            using Quillwright.Templates;

            public record PlainRow(string Title);

            [WordTemplate]
            public partial class BadRowsModel
            {
                [TemplateRows]
                public List<PlainRow> Rows { get; init; } = [];
            }
            """);

        Diagnostic diag = Assert.Single(diagnostics, d => d.Id == "QWT003");
        Assert.NotEqual(Location.None, diag.Location);
        Assert.Equal(0, generated);
    }

    [Fact]
    public void NestedModelWithKeywordMembersAndControlChars_BindsCleanly()
    {
        ITemplateBinder binder = NestedTemplatesContainer.KeywordAndNestedModel.TemplateBinder;
        var model = new NestedTemplatesContainer.KeywordAndNestedModel
        {
            @class = "A-1",
            @event = "Launch",
            @default = true,
        };

        Assert.True(binder.TryGetText(model, "class", out string? cls));
        Assert.Equal("A-1", cls);

        Assert.True(binder.TryGetText(model, "Line\nBreak\"Field", out string? evt));
        Assert.Equal("Launch", evt);

        Assert.True(binder.TryGetCondition(model, "default", out bool cond));
        Assert.True(cond);
    }
}
