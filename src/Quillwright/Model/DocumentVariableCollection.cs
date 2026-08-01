using System.Collections;
using System.Security;
using System.Text;
using System.Xml;

namespace Quillwright.Model;

/// <summary>
/// The named values a document keeps for its own use (<c>w:docVars</c>, ISO/IEC 29500-1
/// §17.15.1.32).
/// </summary>
/// <remarks>
/// The settings part is held as its elements in order rather than modelled child by child, so
/// this is a view over the one element that matters here: reading parses it, writing puts it
/// back at the position the schema wants. There are rarely more than a handful of variables,
/// so re-serialising the element on every change costs nothing worth avoiding.
/// </remarks>
public sealed class DocumentVariableCollection : IReadOnlyCollection<KeyValuePair<string, string>>
{
    private const string ElementName = "docVars";

    private readonly DocumentSettings _settings;

    internal DocumentVariableCollection(DocumentSettings settings) => _settings = settings;

    /// <inheritdoc />
    public int Count => Read().Count;

    /// <summary>The names of the variables, in the order the document lists them.</summary>
    public IEnumerable<string> Names => Read().Keys;

    /// <summary>The value of a variable, or <see langword="null"/> when there is none.</summary>
    /// <param name="name">Name of the variable; matching ignores case, as Word does.</param>
    public string? this[string name]
    {
        get => Read().TryGetValue(name, out string? value) ? value : null;
        set
        {
            Dictionary<string, string> variables = Read();
            if (value is null)
                variables.Remove(name);
            else
                variables[name] = value;

            Write(variables);
        }
    }

    /// <summary>Removes a variable.</summary>
    /// <param name="name">Name of the variable.</param>
    /// <returns><see langword="true"/> when one was there.</returns>
    public bool Remove(string name)
    {
        Dictionary<string, string> variables = Read();
        if (!variables.Remove(name))
            return false;

        Write(variables);
        return true;
    }

    /// <summary>Removes every variable.</summary>
    public void Clear() => _settings.Remove(ElementName);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => Read().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private Dictionary<string, string> Read()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_settings.GetRaw(ElementName) is not { } xml)
            return variables;

        // The element is stored as it sits in the part, where the prefix is declared on the
        // root; read on its own it declares nothing, so the scope is put back around it.
        string scoped = $"<qw:s xmlns:qw=\"urn:quillwright\" xmlns:w=\"{Formats.DocxSchema.NsWord}\">{xml}</qw:s>";
        try
        {
            using var reader = XmlReader.Create(new StringReader(scoped), Xml.XmlDefaults.ReaderSettings);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "docVar" &&
                    Formats.XmlHelp.Attr(reader, "name") is { } name)
                {
                    variables[name] = Formats.XmlHelp.Attr(reader, "val") ?? string.Empty;
                }
            }
        }
        catch (XmlException)
        {
            return variables;
        }

        return variables;
    }

    private void Write(Dictionary<string, string> variables)
    {
        if (variables.Count == 0)
        {
            _settings.Remove(ElementName);
            return;
        }

        var builder = new StringBuilder("<w:docVars>");
        foreach ((string name, string value) in variables)
        {
            builder.Append("<w:docVar w:name=\"").Append(SecurityElement.Escape(name))
                .Append("\" w:val=\"").Append(SecurityElement.Escape(value)).Append("\"/>");
        }

        _settings.SetRaw(ElementName, builder.Append("</w:docVars>").ToString());
    }
}
