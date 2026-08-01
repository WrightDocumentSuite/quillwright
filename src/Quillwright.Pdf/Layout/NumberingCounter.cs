using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>What a list paragraph is preceded by.</summary>
internal readonly record struct NumberLabel(string Text, NumberingLevel Level);

/// <summary>PDF adapter over the core list counter shared with other exporters.</summary>
internal sealed class NumberingCounter
{
    private readonly Quillwright.Rendering.NumberingCounter _inner;

    internal NumberingCounter(NumberingDefinitions numbering) =>
        _inner = new Quillwright.Rendering.NumberingCounter(numbering);

    public NumberLabel? Next(ParagraphFormat format) => Convert(_inner.Next(format));

    public NumberLabel? Peek(ParagraphFormat format) => Convert(_inner.Peek(format));

    private static NumberLabel? Convert(Quillwright.Rendering.NumberLabel? value) =>
        value is { } label ? new NumberLabel(label.Text, label.Level) : null;
}
