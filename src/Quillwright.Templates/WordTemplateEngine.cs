using Quillwright.Model;

namespace Quillwright.Templates;

/// <summary>What the engine did, and what it could not fill.</summary>
/// <param name="ValuesFilled">How many anchors received a value.</param>
/// <param name="RegionsRepeated">How many rows or blocks were produced by repetition.</param>
/// <param name="RegionsRemoved">How many conditional regions were dropped.</param>
/// <param name="UnresolvedNames">Names the template asked for that the model does not have.</param>
public readonly record struct TemplateResult(
    int ValuesFilled,
    int RegionsRepeated,
    int RegionsRemoved,
    IReadOnlyList<string> UnresolvedNames);

/// <summary>
/// Fills a Word template from a typed model.
/// </summary>
/// <remarks>
/// Rendering happens in three passes because each one changes what the next one sees:
/// repeated regions are expanded first, since they create new paragraphs; conditional
/// regions are resolved next, since removing one removes the anchors inside it; and only
/// then are the remaining values filled in. Every pass works on offsets in the paragraph
/// buffer, so a placeholder split across runs by Word's editing history is filled without
/// the caller ever knowing it was split.
/// </remarks>
public static class WordTemplateEngine
{
    /// <summary>Fills a template file and writes the result to another file.</summary>
    /// <typeparam name="TModel">The model type, marked with <see cref="WordTemplateAttribute"/>.</typeparam>
    /// <param name="templatePath">Path to the template document.</param>
    /// <param name="model">The values to fill in.</param>
    /// <param name="outputPath">Where to write the filled document.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public static async ValueTask<TemplateResult> RenderAsync<TModel>(
        string templatePath, TModel model, string outputPath, CancellationToken cancellationToken = default)
        where TModel : ITemplateModel
    {
        WordDocument document = await WordDocument.LoadAsync(templatePath, cancellationToken: cancellationToken).ConfigureAwait(false);
        TemplateResult result = Render(document, model);
        await document.SaveAsync(outputPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Fills a template document in place.</summary>
    /// <typeparam name="TModel">The model type, marked with <see cref="WordTemplateAttribute"/>.</typeparam>
    /// <param name="document">The template document, changed in place.</param>
    /// <param name="model">The values to fill in.</param>
    public static TemplateResult Render<TModel>(WordDocument document, TModel model)
        where TModel : ITemplateModel
    {
        ArgumentNullException.ThrowIfNull(document);
        return Render(document, model!, TModel.TemplateBinder);
    }

    /// <summary>Fills a template document using a binder resolved at run time.</summary>
    /// <param name="document">The template document, changed in place.</param>
    /// <param name="model">The values to fill in.</param>
    /// <param name="binder">Reads the members of the model.</param>
    public static TemplateResult Render(WordDocument document, object model, ITemplateBinder binder)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(binder);

        var state = new RenderState(document, model, binder);
        foreach (BlockContainer container in document.AllContainers.ToArray())
            state.ExpandRepeats(container);

        foreach (BlockContainer container in document.AllContainers.ToArray())
            state.ResolveConditions(container);

        foreach (BlockContainer container in document.AllContainers.ToArray())
            state.FillValues(container, model, binder);

        return state.ToResult();
    }
}
