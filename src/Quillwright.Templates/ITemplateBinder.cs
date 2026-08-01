using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Templates;

/// <summary>A picture a template model supplies, with the size it should be rendered at.</summary>
/// <param name="Image">The image.</param>
/// <param name="Width">Rendered width, or <see langword="null"/> for the natural width.</param>
/// <param name="Height">Rendered height, or <see langword="null"/> for the natural height.</param>
public readonly record struct TemplateImage(ImageData Image, Length? Width = null, Length? Height = null);

/// <summary>The items of a repeated region and the binder that reads them.</summary>
/// <param name="Items">The items, in order.</param>
/// <param name="Binder">Reads the members of one item.</param>
public readonly record struct TemplateRows(IReadOnlyList<object> Items, ITemplateBinder Binder);

/// <summary>
/// Reads the members of a template model by name.
/// </summary>
/// <remarks>
/// The source generator implements this for every type marked with
/// <see cref="WordTemplateAttribute"/>, which is what keeps templating free of reflection:
/// the lookups compile down to a switch on the placeholder name.
/// </remarks>
public interface ITemplateBinder
{
    /// <summary>The names this binder answers to, used to report placeholders a model cannot fill.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>Reads a text value.</summary>
    /// <param name="model">The model instance.</param>
    /// <param name="name">Placeholder name.</param>
    /// <param name="value">The formatted value.</param>
    bool TryGetText(object model, string name, out string? value);

    /// <summary>Reads a repeated collection.</summary>
    /// <param name="model">The model instance.</param>
    /// <param name="name">Collection name.</param>
    /// <param name="rows">The items and their binder.</param>
    bool TryGetRows(object model, string name, out TemplateRows rows);

    /// <summary>Reads a condition.</summary>
    /// <param name="model">The model instance.</param>
    /// <param name="name">Condition name.</param>
    /// <param name="value">Whether the region is kept.</param>
    bool TryGetCondition(object model, string name, out bool value);

    /// <summary>Reads a picture.</summary>
    /// <param name="model">The model instance.</param>
    /// <param name="name">Placeholder name.</param>
    /// <param name="image">The picture and its size.</param>
    bool TryGetImage(object model, string name, out TemplateImage image);
}

/// <summary>
/// Implemented by the source generator on every type marked with
/// <see cref="WordTemplateAttribute"/>.
/// </summary>
public interface ITemplateModel
{
    /// <summary>Reads the members of this model type by name.</summary>
    static abstract ITemplateBinder TemplateBinder { get; }
}
