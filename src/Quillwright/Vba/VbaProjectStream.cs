using System.Text;

namespace Quillwright.Vba;

/// <summary>
/// Reads the <c>PROJECT</c> stream ([MS-OVBA] 2.3.1), a plain text listing that names each
/// module against the kind of thing it belongs to and records the project's protection state.
/// </summary>
/// <remarks>
/// The listing looks like an old configuration file, one <c>Key=Value</c> per line. Four keys
/// introduce a module: the directory stream can only say whether a module stands alone, so this
/// is what separates a class from a form from the code behind the document. Three more keys —
/// <c>CMG</c>, <c>DPB</c> and <c>GC</c> — carry the protection state, each an obfuscated
/// hexadecimal string.
/// </remarks>
internal sealed class VbaProjectStream
{
    /// <summary>What each named module belongs to.</summary>
    public Dictionary<string, VbaModuleKind> Kinds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The obfuscated protection state, from <c>CMG</c>.</summary>
    public string? ProtectionState { get; private set; }

    /// <summary>The obfuscated password, from <c>DPB</c>.</summary>
    public string? Password { get; private set; }

    /// <summary>The obfuscated visibility state, from <c>GC</c>.</summary>
    public string? Visibility { get; private set; }

    /// <summary>The name the project declares for itself.</summary>
    public string? Name { get; private set; }

    /// <summary>Parses the stream.</summary>
    /// <param name="stream">Raw contents of the <c>PROJECT</c> stream, if there is one.</param>
    /// <param name="encoding">Encoding the project's text is written in.</param>
    public static VbaProjectStream Read(byte[]? stream, Encoding encoding)
    {
        var result = new VbaProjectStream();
        if (stream is not { Length: > 0 })
            return result;

        foreach (ReadOnlySpan<char> line in encoding.GetString(stream).AsSpan().EnumerateLines())
        {
            int split = line.IndexOf('=');
            if (split <= 0)
                continue;

            result.Apply(line[..split].Trim(), line[(split + 1)..].Trim());
        }

        return result;
    }

    private void Apply(ReadOnlySpan<char> key, ReadOnlySpan<char> value)
    {
        switch (key)
        {
            case "CMG":
                ProtectionState = value.Trim('"').ToString();
                return;

            case "DPB":
                Password = value.Trim('"').ToString();
                return;

            case "GC":
                Visibility = value.Trim('"').ToString();
                return;

            case "Name":
                Name = value.Trim('"').ToString();
                return;
        }

        if (Kind(key) is not { } kind)
            return;

        // A document module carries its cookie after a slash, as in "ThisDocument/&H00000000".
        int cookie = value.IndexOf('/');
        if (cookie >= 0)
            value = value[..cookie];

        Kinds[value.Trim().Trim('"').ToString()] = kind;
    }

    private static VbaModuleKind? Kind(ReadOnlySpan<char> key) => key switch
    {
        "Module" => VbaModuleKind.Procedural,
        "Document" => VbaModuleKind.Document,
        "Class" => VbaModuleKind.Class,
        "BaseClass" => VbaModuleKind.Form,
        _ => null,
    };
}
