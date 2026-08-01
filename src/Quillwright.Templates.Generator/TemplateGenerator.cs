using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quillwright.Templates.Generator;

/// <summary>
/// Generates a binder for every type marked with <c>[WordTemplate]</c>.
/// </summary>
/// <remarks>
/// The binder turns a placeholder name into a member read through a switch, which is why
/// filling a template needs no reflection and survives trimming and Native AOT. The
/// generator is incremental: it looks only at types carrying the attribute, and the model it
/// builds compares by value, so editing an unrelated file does not re-run it.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class TemplateGenerator : IIncrementalGenerator
{
    private const string TemplateAttribute = "Quillwright.Templates.WordTemplateAttribute";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<TemplateModelInfo?> models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                TemplateAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (syntaxContext, _) => Describe(syntaxContext))
            .Where(static model => model is not null);

        context.RegisterSourceOutput(models, static (production, model) =>
        {
            if (model is not null)
                production.AddSource(model.HintName, TemplateBinderWriter.Write(model));
        });
    }

    private static TemplateModelInfo? Describe(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type)
            return null;

        var members = new EquatableList<TemplateMember>();
        foreach (ISymbol member in EnumerateMembers(type))
        {
            if (Describe(member) is { } described)
                members.Add(described);
        }

        string keyword = type.IsRecord
            ? type.IsValueType ? "record struct" : "record"
            : type.IsValueType ? "struct" : "class";

        return new TemplateModelInfo(
            type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString(),
            type.Name,
            keyword,
            "__" + type.Name + "TemplateBinder",
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            members);
    }

    private static IEnumerable<ISymbol> EnumerateMembers(INamedTypeSymbol type) =>
        type.GetMembers()
            .Where(static member => member.DeclaredAccessibility == Accessibility.Public)
            .Where(static member => member is IPropertySymbol { IsStatic: false, IsIndexer: false } or IFieldSymbol { IsStatic: false, IsConst: false });

    private static TemplateMember? Describe(ISymbol member)
    {
        ITypeSymbol memberType = member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => throw new System.InvalidOperationException(),
        };

        var attributes = member.GetAttributes().ToList();
        if (Find(attributes, "TemplateIgnoreAttribute") is not null || member.Name == "EqualityContract")
            return null;

        AttributeData? rows = Find(attributes, "TemplateRowsAttribute");
        AttributeData? condition = Find(attributes, "TemplateConditionAttribute");
        AttributeData? image = Find(attributes, "TemplateImageAttribute");
        AttributeData? field2 = Find(attributes, "TemplateFieldAttribute");

        MemberRole role = rows is not null ? MemberRole.Rows
            : condition is not null ? MemberRole.Condition
            : image is not null ? MemberRole.Image
            : Infer(memberType);

        string name = NameFrom(rows ?? condition ?? image ?? field2) ?? member.Name;
        string? rowItem = role == MemberRole.Rows ? ElementTypeOf(memberType)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : null;
        if (role == MemberRole.Rows && rowItem is null)
            return null;

        return new TemplateMember(
            member.Name,
            name,
            role,
            NamedArgument(field2, "Format") as string,
            rowItem is null ? null : rowItem + ".TemplateBinder",
            rowItem,
            NamedArgument(image, "WidthCentimeters") is double width ? width : 0,
            NamedArgument(image, "HeightCentimeters") is double height ? height : 0,
            memberType.NullableAnnotation == NullableAnnotation.Annotated || memberType.IsReferenceType);
    }

    private static MemberRole Infer(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Boolean)
            return MemberRole.Condition;
        if (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Quillwright.Model.ImageData")
            return MemberRole.Image;
        return MemberRole.Text;
    }

    private static ITypeSymbol? ElementTypeOf(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return array.ElementType;

        foreach (INamedTypeSymbol candidate in type.AllInterfaces.Concat(type is INamedTypeSymbol named ? [named] : []))
        {
            if (candidate is { IsGenericType: true, TypeArguments.Length: 1 } &&
                candidate.ConstructedFrom.ToDisplayString().StartsWith("System.Collections.Generic.IEnumerable<", System.StringComparison.Ordinal))
            {
                return candidate.TypeArguments[0];
            }
        }

        return null;
    }

    private static AttributeData? Find(List<AttributeData> attributes, string name) =>
        attributes.FirstOrDefault(a => a.AttributeClass?.Name == name);

    private static string? NameFrom(AttributeData? attribute)
    {
        if (attribute is null)
            return null;
        if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string positional)
            return positional;
        return NamedArgument(attribute, "Name") as string;
    }

    private static object? NamedArgument(AttributeData? attribute, string name) =>
        attribute?.NamedArguments.FirstOrDefault(a => a.Key == name).Value.Value;
}
