using Quillwright.IO;
using Quillwright.Model;

namespace Quillwright.Doc;

/// <summary>
/// Maps a document's properties — its title, author, dates and the custom fields a document
/// management system adds — onto the property sets every compound file carries alongside the
/// document itself.
/// </summary>
/// <remarks>
/// These are not part of the Word format at all: they are streams of their own that the shell
/// and the file properties dialog read without opening the document. A file without them still
/// opens; it just claims to have no author. The custom properties live in the second set of
/// the document summary stream, where each is found by a name the set's own dictionary gives
/// it ([MS-OLEPS] 2.18.1).
/// </remarks>
internal static class DocSummary
{
    private const int Title = 0x02;
    private const int Subject = 0x03;
    private const int Author = 0x04;
    private const int Keywords = 0x05;
    private const int Comments = 0x06;
    private const int LastAuthor = 0x08;
    private const int Revision = 0x09;
    private const int Created = 0x0C;
    private const int LastSaved = 0x0D;

    private const int Company = 0x0F;
    private const int Manager = 0x0E;
    private const int Category = 0x02;
    private const int ContentStatus = 0x1B;

    /// <summary>The first identifier a named property may use ([MS-OLEPS] 2.16).</summary>
    private const int FirstNamedId = 2;

    /// <summary>Builds the summary stream, or nothing when the document declares no properties.</summary>
    /// <param name="properties">The document's properties.</param>
    public static byte[] Build(DocumentProperties properties)
    {
        if (properties.IsEmpty)
            return [];

        var summary = new PropertySetSection(PropertySetStream.SummaryFormat);
        Add(summary, Title, properties.Title);
        Add(summary, Subject, properties.Subject);
        Add(summary, Author, properties.Creator);
        Add(summary, Keywords, properties.Keywords);
        Add(summary, Comments, properties.Description);
        Add(summary, LastAuthor, properties.LastModifiedBy);
        Add(summary, Revision, properties.Revision);
        Add(summary, Created, properties.Created);
        Add(summary, LastSaved, properties.Modified);

        return summary.IsEmpty ? [] : PropertySetStream.Build(summary);
    }

    /// <summary>
    /// Builds the document summary stream, which carries the company and the custom
    /// properties, or nothing when there is neither.
    /// </summary>
    /// <param name="properties">The document's core properties, for the fields this set owns.</param>
    /// <param name="extended">The application properties.</param>
    /// <param name="custom">The custom properties.</param>
    public static byte[] BuildDocumentSummary(
        DocumentProperties properties, ExtendedProperties extended, CustomPropertyCollection custom)
    {
        var summary = new PropertySetSection(PropertySetStream.DocumentSummaryFormat);
        Add(summary, Category, properties.Category);
        Add(summary, ContentStatus, properties.ContentStatus);
        Add(summary, Company, extended.Company);
        Add(summary, Manager, extended.Manager);

        var user = new PropertySetSection(PropertySetStream.UserDefinedFormat);
        int id = FirstNamedId;
        foreach (CustomProperty property in custom)
        {
            if (property.Value.IsEmpty)
                continue;
            user.Names[id] = property.Name;
            user.Values[id] = property.Value;
            id++;
        }

        if (summary.IsEmpty && user.IsEmpty)
            return [];

        return user.IsEmpty
            ? PropertySetStream.Build(summary)
            : PropertySetStream.Build(summary, user);
    }

    /// <summary>Applies the summary stream to a document's properties.</summary>
    /// <param name="stream">The summary stream, or <see langword="null"/> when there is none.</param>
    /// <param name="properties">The properties to fill in place.</param>
    public static void Apply(byte[]? stream, DocumentProperties properties)
    {
        if (PropertySetStream.Read(stream).FirstOrDefault() is not { } summary)
            return;

        properties.Title = Text(summary, Title) ?? properties.Title;
        properties.Subject = Text(summary, Subject) ?? properties.Subject;
        properties.Creator = Text(summary, Author) ?? properties.Creator;
        properties.Keywords = Text(summary, Keywords) ?? properties.Keywords;
        properties.Description = Text(summary, Comments) ?? properties.Description;
        properties.LastModifiedBy = Text(summary, LastAuthor) ?? properties.LastModifiedBy;
        properties.Revision = Text(summary, Revision) ?? properties.Revision;
        properties.Created = summary.Values.GetValueOrDefault(Created).AsDateTime() ?? properties.Created;
        properties.Modified = summary.Values.GetValueOrDefault(LastSaved).AsDateTime() ?? properties.Modified;
    }

    /// <summary>Applies the document summary stream, which holds the company and the custom properties.</summary>
    /// <param name="stream">The document summary stream, or <see langword="null"/> when there is none.</param>
    /// <param name="document">The document to fill in place.</param>
    public static void ApplyDocumentSummary(byte[]? stream, WordDocument document)
    {
        List<PropertySetSection> sections = PropertySetStream.Read(stream);
        if (sections.Count == 0)
            return;

        PropertySetSection summary = sections[0];
        document.Properties.Category ??= Text(summary, Category);
        document.Properties.ContentStatus ??= Text(summary, ContentStatus);
        document.ApplicationProperties.Company ??= Text(summary, Company);
        document.ApplicationProperties.Manager ??= Text(summary, Manager);

        if (sections.Count < 2)
            return;

        foreach ((int id, string name) in sections[1].Names.OrderBy(static entry => entry.Key))
        {
            if (sections[1].Values.TryGetValue(id, out PropertyValue value))
                document.CustomProperties.Set(name, value);
        }
    }

    private static void Add(PropertySetSection section, int id, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            section.Values[id] = PropertyValue.FromText(value);
    }

    private static void Add(PropertySetSection section, int id, DateTimeOffset? value)
    {
        if (value is { } moment)
            section.Values[id] = PropertyValue.FromDateTime(moment);
    }

    private static string? Text(PropertySetSection section, int id) =>
        section.Values.GetValueOrDefault(id).AsText() is { Length: > 0 } text ? text : null;
}
