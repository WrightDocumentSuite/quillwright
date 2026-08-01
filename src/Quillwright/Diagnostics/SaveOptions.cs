namespace Quillwright.Diagnostics;

/// <summary>
/// Controls how a document is written.
/// </summary>
public sealed class SaveOptions
{
    /// <summary>Shared instance with default settings.</summary>
    public static SaveOptions Default { get; } = new();

    /// <summary>
    /// Writes the parts and markup that were preserved on load. Default is <see langword="true"/>.
    /// </summary>
    public bool WritePreservedContent { get; init; } = true;

    /// <summary>
    /// Updates <c>docProps/core.xml</c> modification metadata on save. Default is
    /// <see langword="true"/>; turn it off for byte-stable output in tests.
    /// </summary>
    public bool UpdateModifiedTimestamp { get; init; } = true;

    /// <summary>
    /// Locks the saved document behind a password ([MS-OFFCRYPTO] 2.3.4.10). The result is a
    /// compound file holding the encrypted package rather than the package itself, which is
    /// what Word writes and what it expects to be handed back.
    /// </summary>
    /// <remarks>
    /// This is encryption of the file, not the document protection of
    /// <see cref="Model.DocumentProtectionSettings"/>: without the password the content cannot
    /// be read at all, by Word or by anything else.
    /// </remarks>
    public string? Password { get; init; }
}
