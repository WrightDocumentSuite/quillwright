namespace Quillwright.Model;

/// <summary>
/// The core properties of a document (<c>docProps/core.xml</c>): the fields a file browser
/// or a document management system reads without opening the document.
/// </summary>
public sealed class DocumentProperties
{
    /// <summary>Title of the document.</summary>
    public string? Title { get; set; }

    /// <summary>Subject of the document.</summary>
    public string? Subject { get; set; }

    /// <summary>Who created the document.</summary>
    public string? Creator { get; set; }

    /// <summary>Search keywords.</summary>
    public string? Keywords { get; set; }

    /// <summary>Free-form description.</summary>
    public string? Description { get; set; }

    /// <summary>Who saved the document last.</summary>
    public string? LastModifiedBy { get; set; }

    /// <summary>Revision counter, incremented by Word on each save.</summary>
    public string? Revision { get; set; }

    /// <summary>When the document was created.</summary>
    public DateTimeOffset? Created { get; set; }

    /// <summary>When the document was last saved.</summary>
    public DateTimeOffset? Modified { get; set; }

    /// <summary>Category the document belongs to.</summary>
    public string? Category { get; set; }

    /// <summary>Workflow status.</summary>
    public string? ContentStatus { get; set; }

    /// <summary>Language of the content.</summary>
    public string? Language { get; set; }

    /// <summary>Returns <see langword="true"/> when nothing is set.</summary>
    public bool IsEmpty =>
        Title is null && Subject is null && Creator is null && Keywords is null && Description is null &&
        LastModifiedBy is null && Revision is null && Created is null && Modified is null &&
        Category is null && ContentStatus is null && Language is null;
}
