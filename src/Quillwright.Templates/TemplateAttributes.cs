namespace Quillwright.Templates;

/// <summary>
/// Marks a type as a template model. The source generator implements
/// <see cref="ITemplateModel"/> for it, so filling a document uses no reflection and works
/// under Native AOT.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class WordTemplateAttribute : Attribute;

/// <summary>Overrides the placeholder name or the formatting of a member.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, Inherited = false)]
public sealed class TemplateFieldAttribute : Attribute
{
    /// <summary>Creates the attribute with the default name.</summary>
    public TemplateFieldAttribute()
    {
    }

    /// <summary>Creates the attribute with an explicit placeholder name.</summary>
    /// <param name="name">Name used in the template, without the braces.</param>
    public TemplateFieldAttribute(string name) => Name = name;

    /// <summary>Name used in the template, or <see langword="null"/> to use the member name.</summary>
    public string? Name { get; init; }

    /// <summary>A standard or custom format string applied to the value.</summary>
    public string? Format { get; init; }
}

/// <summary>
/// Marks a collection whose items repeat a table row or a block of content. The item type
/// must itself carry <see cref="WordTemplateAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, Inherited = false)]
public sealed class TemplateRowsAttribute : Attribute
{
    /// <summary>Creates the attribute with the default name.</summary>
    public TemplateRowsAttribute()
    {
    }

    /// <summary>Creates the attribute with an explicit collection name.</summary>
    /// <param name="name">Name used in the template.</param>
    public TemplateRowsAttribute(string name) => Name = name;

    /// <summary>Name used in the template, or <see langword="null"/> to use the member name.</summary>
    public string? Name { get; init; }
}

/// <summary>Marks a boolean member that switches a conditional region of the template on or off.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, Inherited = false)]
public sealed class TemplateConditionAttribute : Attribute
{
    /// <summary>Creates the attribute with the default name.</summary>
    public TemplateConditionAttribute()
    {
    }

    /// <summary>Creates the attribute with an explicit condition name.</summary>
    /// <param name="name">Name used in the template.</param>
    public TemplateConditionAttribute(string name) => Name = name;

    /// <summary>Name used in the template, or <see langword="null"/> to use the member name.</summary>
    public string? Name { get; init; }
}

/// <summary>Marks an <see cref="Model.ImageData"/> member that fills a placeholder with a picture.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, Inherited = false)]
public sealed class TemplateImageAttribute : Attribute
{
    /// <summary>Creates the attribute with the default name.</summary>
    public TemplateImageAttribute()
    {
    }

    /// <summary>Creates the attribute with an explicit placeholder name.</summary>
    /// <param name="name">Name used in the template.</param>
    public TemplateImageAttribute(string name) => Name = name;

    /// <summary>Name used in the template, or <see langword="null"/> to use the member name.</summary>
    public string? Name { get; init; }

    /// <summary>Rendered width in centimetres, or zero for the image's natural width.</summary>
    public double WidthCentimeters { get; init; }

    /// <summary>Rendered height in centimetres, or zero for the image's natural height.</summary>
    public double HeightCentimeters { get; init; }
}

/// <summary>Keeps a member out of the template contract.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, Inherited = false)]
public sealed class TemplateIgnoreAttribute : Attribute;
