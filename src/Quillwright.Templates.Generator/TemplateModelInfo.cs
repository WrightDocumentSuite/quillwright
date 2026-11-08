using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Quillwright.Templates.Generator;

/// <summary>What a member contributes to a template.</summary>
internal enum MemberRole
{
    Text,
    Rows,
    Condition,
    Image,
}

/// <summary>One enclosing partial type declaration for a nested template model.</summary>
internal readonly record struct ContainerTypeInfo(string Keyword, string Name);

/// <summary>One diagnostic discovered while inspecting a template model.</summary>
internal readonly record struct TemplateDiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    Location Location,
    string TargetName,
    string? MemberName,
    string? Detail);

/// <summary>One member of a template model, as the generator sees it.</summary>
internal readonly record struct TemplateMember(
    string Accessor,
    string Name,
    MemberRole Role,
    string? Format,
    string? RowBinderType,
    string? RowItemType,
    double WidthCentimeters,
    double HeightCentimeters,
    bool IsNullable);

/// <summary>A template model type and everything needed to generate its binder.</summary>
internal sealed record TemplateModelInfo(
    string Namespace,
    string TypeName,
    string TypeKeyword,
    string FullTypeName,
    string HintName,
    EquatableList<ContainerTypeInfo> Containers,
    EquatableList<TemplateMember> Members,
    EquatableList<TemplateDiagnosticInfo> Diagnostics);

/// <summary>
/// A list with value equality, so the incremental generator's cache can tell whether a
/// model actually changed rather than re-running on every keystroke.
/// </summary>
internal sealed class EquatableList<T> : List<T>, System.IEquatable<EquatableList<T>>
{
    public EquatableList()
    {
    }

    public EquatableList(IEnumerable<T> items) : base(items)
    {
    }

    public bool Equals(EquatableList<T>? other) => other is not null && this.SequenceEqual(other);

    public override bool Equals(object? obj) => Equals(obj as EquatableList<T>);

    public override int GetHashCode()
    {
        int hash = Count;
        foreach (T item in this)
            hash = (hash * 397) ^ (item?.GetHashCode() ?? 0);
        return hash;
    }
}
