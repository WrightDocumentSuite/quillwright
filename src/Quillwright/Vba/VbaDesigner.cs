using System.Globalization;
using System.Text;
using Quillwright.IO;
using Quillwright.Primitives;

namespace Quillwright.Vba;

/// <summary>
/// The design-time properties of a user form: the outer frame out of its <c>VBFrame</c> stream
/// ([MS-OVBA] 2.3.5), and the controls on it out of the storage beside it ([MS-OFORMS]).
/// </summary>
/// <remarks>
/// A form module is two things: the code, which reads like any other module, and a storage
/// beside it holding what the form looks like. The caption and the size the designer was left
/// at are written in plain text; everything on the form is written in a binary format of its
/// own, which <see cref="Controls"/> decodes.
/// </remarks>
public sealed class VbaDesigner
{
    private VbaDesigner(string caption, Length width, Length height, string classId)
    {
        Caption = caption;
        Width = width;
        Height = height;
        ClassId = classId;
    }

    /// <summary>The title text the form was given at design time.</summary>
    public string Caption { get; }

    /// <summary>The form's width.</summary>
    public Length Width { get; }

    /// <summary>The form's height.</summary>
    public Length Height { get; }

    /// <summary>
    /// Class identifier of the designer. An Office user form is
    /// <c>{C62A69F0-16DC-11CE-9E98-00AA00574A4F}</c>.
    /// </summary>
    public string ClassId { get; }

    /// <summary>
    /// What was laid out on the form: the controls, their places and what the designer put in
    /// them. <see langword="null"/> when the layout could not be read.
    /// </summary>
    public VbaFormControl? Controls { get; internal set; }

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"\"{Caption}\" {Width.Points:0.#}x{Height.Points:0.#}pt");

    /// <summary>Reads the designer properties of a module, if it has any.</summary>
    /// <param name="container">The compound file the project lives in.</param>
    /// <param name="root">Path prefix of the project inside the container.</param>
    /// <param name="streamName">Name of the module, which is also the name of its storage.</param>
    /// <param name="encoding">Encoding of the project's single-byte text.</param>
    /// <returns>The properties, or <see langword="null"/> when the module is not a designer.</returns>
    internal static VbaDesigner? Read(CompoundFile container, string root, string streamName, Encoding encoding)
    {
        // The stream is named with a leading UTF-16 character 0x0003 rather than a letter.
        if (container.ReadStream($"{root}{streamName}/\u0003VBFrame") is not { Length: > 0 } stream)
            return null;

        string caption = string.Empty;
        string classId = string.Empty;
        Length width = default;
        Length height = default;

        foreach (ReadOnlySpan<char> line in encoding.GetString(stream).AsSpan().EnumerateLines())
        {
            ReadOnlySpan<char> text = line.Trim();
            if (text.StartsWith("Begin ", StringComparison.OrdinalIgnoreCase))
            {
                classId = FirstWord(text[6..]);
                continue;
            }

            int split = text.IndexOf('=');
            if (split <= 0)
                continue;

            ReadOnlySpan<char> value = WithoutComment(text[(split + 1)..]);
            switch (text[..split].Trim())
            {
                case "Caption": caption = value.Trim().Trim('"').ToString(); break;
                case "ClientWidth": width = Twips(value); break;
                case "ClientHeight": height = Twips(value); break;
            }
        }

        return new VbaDesigner(caption, width, height, classId)
        {
            Controls = OForms.VbaFormReader.Read(container, root + streamName),
        };
    }

    /// <summary>Removes the trailing comment a property line may carry.</summary>
    /// <param name="value">Everything after the equals sign.</param>
    private static ReadOnlySpan<char> WithoutComment(ReadOnlySpan<char> value)
    {
        // A comment opens at an apostrophe, but only one outside the quoted caption.
        bool quoted = false;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '"')
                quoted = !quoted;
            else if (value[i] == '\'' && !quoted)
                return value[..i].Trim();
        }

        return value.Trim();
    }

    private static string FirstWord(ReadOnlySpan<char> text)
    {
        int end = text.IndexOf(' ');
        return (end < 0 ? text : text[..end]).Trim().ToString();
    }

    /// <summary>Sizes in a <c>VBFrame</c> stream are written as a decimal count of twips.</summary>
    /// <param name="value">The value as written.</param>
    private static Length Twips(ReadOnlySpan<char> value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            ? Length.FromTwips((int)Math.Round(result))
            : default;
}
