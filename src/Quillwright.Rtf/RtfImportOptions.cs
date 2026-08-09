using Quillwright.Diagnostics;

namespace Quillwright.Rtf;

/// <summary>Controls resource limits and optional content while importing RTF.</summary>
public sealed record RtfImportOptions
{
    private DocumentLoadBudget _budget = DocumentLoadBudget.Default;

    /// <summary>The options used when a caller passes none.</summary>
    public static RtfImportOptions Default { get; } = new();

    /// <summary>The common document-load budget used by the RTF parser.</summary>
    public DocumentLoadBudget Budget
    {
        get => _budget;
        init => _budget = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Largest accepted input, in bytes. This backward-compatible alias updates
    /// <see cref="DocumentLoadBudget.MaxInputBytes"/> on <see cref="Budget"/>.
    /// </summary>
    public int MaxInputBytes
    {
        get => (int)Math.Min(_budget.MaxInputBytes, int.MaxValue);
        init => _budget = _budget with { MaxInputBytes = value };
    }

    /// <summary>Largest brace nesting depth. The default is 256 groups.</summary>
    public int MaxGroupDepth
    {
        get => _budget.MaxMarkupDepth;
        init => _budget = _budget with { MaxMarkupDepth = value };
    }

    /// <summary>Largest amount of decoded document text. The default is 16 million UTF-16 code units.</summary>
    public int MaxTextCharacters
    {
        get => _budget.MaxTextCharacters;
        init => _budget = _budget with { MaxTextCharacters = value };
    }

    internal void Validate()
    {
        _budget.Validate();
    }
}
