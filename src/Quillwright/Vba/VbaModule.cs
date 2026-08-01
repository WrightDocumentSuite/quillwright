namespace Quillwright.Vba;

/// <summary>What a VBA module is attached to.</summary>
public enum VbaModuleKind
{
    /// <summary>A standard module holding procedures that stand on their own.</summary>
    Procedural,

    /// <summary>A module behind the document itself, where document events are handled.</summary>
    Document,

    /// <summary>A class module.</summary>
    Class,

    /// <summary>The code behind a user form.</summary>
    Form,
}

/// <summary>One module of a VBA project, with its source.</summary>
/// <param name="name">Name the module goes by in the editor.</param>
/// <param name="streamName">Name of the stream the module is stored in.</param>
/// <param name="kind">What the module is attached to.</param>
/// <param name="code">The module's source, with the line endings it was written with.</param>
public sealed class VbaModule(string name, string streamName, VbaModuleKind kind, string code)
{
    /// <summary>Name the module goes by in the editor.</summary>
    public string Name { get; } = name;

    /// <summary>Name of the stream the module is stored in, which may differ from its name.</summary>
    public string StreamName { get; } = streamName;

    /// <summary>What the module is attached to.</summary>
    public VbaModuleKind Kind { get; } = kind;

    /// <summary>The description the author gave the module, if any.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the module is marked read-only.</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Whether the module is usable only from inside its own project.</summary>
    public bool IsPrivate { get; init; }

    /// <summary>
    /// The design-time properties of the form this module sits behind, when it is one. Only the
    /// outer properties are read; the layout of the controls is a format of its own.
    /// </summary>
    public VbaDesigner? Designer { get; init; }

    /// <summary>
    /// The module's source, exactly as stored — which is to say with the <c>Attribute</c> lines
    /// the editor hides, making the text the same as a <c>.bas</c> export and importable as one.
    /// </summary>
    public string Code { get; } = code;

    /// <summary>
    /// Whether the module holds nothing but its attribute preamble. Word gives every document
    /// a module whether or not anything was written in it, so this is what tells a document
    /// that merely could run macros from one that does.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            foreach (ReadOnlySpan<char> line in Code.AsSpan().EnumerateLines())
            {
                ReadOnlySpan<char> text = line.Trim();
                if (!text.IsEmpty && !text.StartsWith("Attribute ", StringComparison.Ordinal))
                    return false;
            }

            return true;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({Kind}, {Code.Length} chars)";
}
