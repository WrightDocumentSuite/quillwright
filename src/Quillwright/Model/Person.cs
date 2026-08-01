namespace Quillwright.Model;

/// <summary>
/// Who an author name belongs to: the identity behind the string that appears on a comment or
/// a tracked change ([MS-DOCX] 2.5.3.5, <c>people.xml</c>).
/// </summary>
/// <remarks>
/// A comment records its author as free text, so two people called "A. Smith" are the same
/// author as far as the comments part is concerned. The people part resolves that by naming
/// the identity provider and the account, which is what lets Word show a photograph beside a
/// comment and tell one reviewer from another across a merge.
/// </remarks>
public sealed class Person
{
    /// <summary>Creates an identity for an author name.</summary>
    /// <param name="author">The author name as it appears on comments and revisions.</param>
    public Person(string author) => Author = author;

    /// <summary>
    /// The author name this identity belongs to. It matches the <c>w:author</c> of at least
    /// one comment or revision in the document.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Which directory the account comes from: <c>None</c>, <c>AD</c>, <c>Windows Live</c>,
    /// or an Office 365 tenant. <see langword="null"/> when the document says nothing.
    /// </summary>
    public string? ProviderId { get; set; }

    /// <summary>
    /// The account within <see cref="ProviderId"/>. For <c>None</c> it is the author's own
    /// name; for <c>AD</c> a security identifier; for the rest a provider-specific string.
    /// </summary>
    public string? UserId { get; set; }
}
