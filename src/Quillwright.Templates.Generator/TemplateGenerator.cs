using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    private const string TemplateModelInterface = "Quillwright.Templates.ITemplateModel";

    private static readonly DiagnosticDescriptor MustBePartial = new(
        "QWT001",
        "A template model and its containing types must be partial",
        "'{0}' is marked with [WordTemplate] but it or one of its containing types is not declared partial, so the template binder cannot be generated",
        "Quillwright.Templates",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedTemplateType = new(
        "QWT002",
        "A template model type cannot be generated",
        "'{0}' cannot be used as a [WordTemplate] model: {1}",
        "Quillwright.Templates",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidTemplateRowsMember = new(
        "QWT003",
        "A [TemplateRows] member must be a sequence of template models",
        "'{0}.{1}' is marked with [TemplateRows], but '{2}' is not a sequence of [WordTemplate] models",
        "Quillwright.Templates",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly SymbolDisplayFormat FullyQualifiedWithEscapedKeywords = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

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
            if (model is null)
                return;

            foreach (TemplateDiagnosticInfo diagnostic in model.Diagnostics)
            {
                Diagnostic reported = diagnostic.MemberName is not null
                    ? Diagnostic.Create(
                        diagnostic.Descriptor,
                        diagnostic.Location,
                        diagnostic.TargetName,
                        diagnostic.MemberName,
                        diagnostic.Detail ?? string.Empty)
                    : diagnostic.Detail is not null
                        ? Diagnostic.Create(
                            diagnostic.Descriptor,
                            diagnostic.Location,
                            diagnostic.TargetName,
                            diagnostic.Detail)
                        : Diagnostic.Create(
                            diagnostic.Descriptor,
                            diagnostic.Location,
                            diagnostic.TargetName);
                production.ReportDiagnostic(reported);
            }

            if (model.Diagnostics.Count == 0)
                production.AddSource(model.HintName, TemplateBinderWriter.Write(model));
        });
    }

    private static TemplateModelInfo? Describe(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type ||
            context.TargetNode is not TypeDeclarationSyntax declaration)
        {
            return null;
        }

        string displayName = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        Location typeLocation = declaration.Identifier.GetLocation();
        var diagnostics = new EquatableList<TemplateDiagnosticInfo>();

        bool isPartial = IsPartial(type);
        var containers = new EquatableList<ContainerTypeInfo>();
        for (INamedTypeSymbol? container = type.ContainingType; container is not null; container = container.ContainingType)
        {
            containers.Insert(0, new ContainerTypeInfo(DeclarationKeyword(container), DeclarationName(container)));
            isPartial &= IsPartial(container);
        }

        if (!isPartial)
        {
            diagnostics.Add(new TemplateDiagnosticInfo(MustBePartial, typeLocation, displayName, null, null));
        }

        if (UnsupportedShapeReason(type) is { } shapeReason)
        {
            diagnostics.Add(new TemplateDiagnosticInfo(UnsupportedTemplateType, typeLocation, displayName, null, shapeReason));
        }

        var members = new EquatableList<TemplateMember>();
        foreach (ISymbol member in EnumerateMembers(type))
        {
            if (DescribeMember(member, displayName, diagnostics) is { } described)
                members.Add(described);
        }

        return new TemplateModelInfo(
            type.ContainingNamespace.IsGlobalNamespace ? string.Empty : NamespaceName(type.ContainingNamespace),
            DeclarationName(type),
            DeclarationKeyword(type),
            type.ToDisplayString(FullyQualifiedWithEscapedKeywords),
            BuildHintName(type),
            containers,
            members,
            diagnostics);
    }

    private static bool IsPartial(INamedTypeSymbol type) => type.DeclaringSyntaxReferences.All(reference =>
        reference.GetSyntax() is TypeDeclarationSyntax declaration &&
        declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)));

    private static string? UnsupportedShapeReason(INamedTypeSymbol type)
    {
        if (type.TypeKind is not TypeKind.Class and not TypeKind.Struct)
            return "only classes, structs, and records can be template models";
        if (type.IsStatic)
            return "static types cannot implement ITemplateModel";
        if (type.IsAbstract)
            return "abstract types cannot be instantiated as template models";
        if (type.IsRefLikeType)
            return "ref-like types cannot implement ITemplateModel";
        if (type.IsFileLocal || ContainingTypes(type).Any(static container => container.IsFileLocal))
            return "file-local types cannot be extended from a generated source file";

        return null;
    }

    private static IEnumerable<INamedTypeSymbol> ContainingTypes(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type.ContainingType; current is not null; current = current.ContainingType)
            yield return current;
    }

    private static IEnumerable<ISymbol> EnumerateMembers(INamedTypeSymbol type) =>
        type.GetMembers()
            .Where(static member => member.DeclaredAccessibility == Accessibility.Public)
            .Where(static member => member is IPropertySymbol { IsStatic: false, IsIndexer: false, GetMethod: not null }
                or IFieldSymbol { IsStatic: false, IsConst: false });

    private static TemplateMember? DescribeMember(
        ISymbol member,
        string ownerDisplayName,
        EquatableList<TemplateDiagnosticInfo> diagnostics)
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
        string? rowItem = null;
        if (role == MemberRole.Rows)
        {
            ITypeSymbol? elementType = ElementTypeOf(memberType);
            if (elementType is null || !IsTemplateModelType(elementType))
            {
                Location memberLocation = member.Locations.FirstOrDefault() ?? Location.None;
                diagnostics.Add(new TemplateDiagnosticInfo(
                    InvalidTemplateRowsMember,
                    memberLocation,
                    ownerDisplayName,
                    member.Name,
                    memberType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return null;
            }

            rowItem = elementType.ToDisplayString(FullyQualifiedWithEscapedKeywords);
        }

        return new TemplateMember(
            EscapeIdentifier(member.Name),
            name,
            role,
            NamedArgument(field2, "Format") as string,
            rowItem is null ? null : rowItem + ".TemplateBinder",
            rowItem,
            NamedArgument(image, "WidthCentimeters") is double width ? width : 0,
            NamedArgument(image, "HeightCentimeters") is double height ? height : 0,
            memberType.NullableAnnotation == NullableAnnotation.Annotated || memberType.IsReferenceType);
    }

    private static bool IsTemplateModelType(ITypeSymbol type)
    {
        if (type.GetAttributes().Any(static attr => attr.AttributeClass?.ToDisplayString() == TemplateAttribute))
            return true;

        return type.AllInterfaces.Any(static iface => iface.ToDisplayString() == TemplateModelInterface);
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

    private static string DeclarationKeyword(INamedTypeSymbol type) => type.TypeKind == TypeKind.Interface
        ? "interface"
        : type.IsRecord
            ? type.IsValueType ? "record struct" : "record"
            : type.IsValueType ? "struct" : "class";

    private static string DeclarationName(INamedTypeSymbol type)
    {
        var name = new StringBuilder(EscapeIdentifier(type.Name));
        if (type.TypeParameters.Length == 0)
            return name.ToString();

        name.Append('<');
        for (int i = 0; i < type.TypeParameters.Length; i++)
        {
            if (i > 0)
                name.Append(", ");

            ITypeParameterSymbol parameter = type.TypeParameters[i];
            if (parameter.Variance == VarianceKind.In)
                name.Append("in ");
            else if (parameter.Variance == VarianceKind.Out)
                name.Append("out ");

            name.Append(EscapeIdentifier(parameter.Name));
        }

        return name.Append('>').ToString();
    }

    private static string NamespaceName(INamespaceSymbol value)
    {
        var parts = new Stack<string>();
        for (INamespaceSymbol? current = value; current is { IsGlobalNamespace: false }; current = current.ContainingNamespace)
            parts.Push(EscapeIdentifier(current.Name));

        return string.Join(".", parts);
    }

    internal static string EscapeIdentifier(string identifier) =>
        SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ||
        SyntaxFacts.GetContextualKeywordKind(identifier) != SyntaxKind.None
            ? "@" + identifier
            : identifier;

    private static string BuildHintName(INamedTypeSymbol type)
    {
        var parts = new Stack<string>();
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
            parts.Push(current.MetadataName);

        string metadataName = type.ContainingNamespace.IsGlobalNamespace
            ? string.Join("+", parts)
            : type.ContainingNamespace.ToDisplayString() + "::" + string.Join("+", parts);

        var hint = new StringBuilder((metadataName.Length * 4) + 24).Append("QWT_");
        foreach (char character in metadataName)
            hint.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));

        return hint.Append(".Template.g.cs").ToString();
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
