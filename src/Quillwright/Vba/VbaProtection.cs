using System.Text;

namespace Quillwright.Vba;

/// <summary>
/// Whether a VBA project was locked, and by whom ([MS-OVBA] 2.3.1.15 to 2.3.1.17).
/// </summary>
/// <remarks>
/// None of this guards the source. The lock is a instruction to the editor, recorded beside the
/// code rather than over it, so a locked project reads exactly like an open one. What it is good
/// for is telling you what the author intended, and — where the file stores the password as text
/// rather than as a hash — what the password was.
/// </remarks>
public sealed class VbaProtection
{
    private readonly VbaPasswordHash? _hash;
    private readonly Encoding _encoding;

    private VbaProtection(Encoding encoding, VbaPasswordHash? hash)
    {
        _encoding = encoding;
        _hash = hash;
    }

    /// <summary>A project that declares no protection at all.</summary>
    public static VbaProtection None { get; } = new(Encoding.Latin1, null);

    /// <summary>Whether the author asked for the project to be protected.</summary>
    public bool IsUserProtected { get; private init; }

    /// <summary>Whether the host application asked for the project to be protected.</summary>
    public bool IsHostProtected { get; private init; }

    /// <summary>Whether the editor asked for the project to be protected.</summary>
    public bool IsEditorProtected { get; private init; }

    /// <summary>Whether a password is set on the project.</summary>
    public bool HasPassword { get; private init; }

    /// <summary>
    /// Whether the project shows in the editor. A project hidden this way is always protected
    /// by the editor as well, so this is the last part of a lock rather than a display setting.
    /// </summary>
    public bool IsVisible { get; private init; } = true;

    /// <summary>
    /// The password itself, on the files that keep it as text rather than as a hash. Usually
    /// <see langword="null"/>, because a hash is the normal choice and cannot be undone.
    /// </summary>
    public string? Password { get; private init; }

    /// <summary>Whether anything at all asked for the project to be protected.</summary>
    public bool IsProtected => IsUserProtected || IsHostProtected || IsEditorProtected || HasPassword;

    /// <summary>
    /// Whether a password is the one set on the project. The stored hash cannot be turned back
    /// into a password, but a candidate can be put through the same steps and the results
    /// compared ([MS-OVBA] 2.4.4.5), which is what makes a known password checkable.
    /// </summary>
    /// <param name="password">The password to try.</param>
    /// <returns><see langword="false"/> when there is no password, or when it is not this one.</returns>
    public bool IsPasswordCorrect(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (Password is not null)
            return string.Equals(Password, password, StringComparison.Ordinal);

        return _hash is not null && _hash.Matches(_encoding.GetBytes(password));
    }

    /// <summary>Reads the protection values out of the <c>PROJECT</c> stream.</summary>
    /// <param name="state">The <c>CMG</c> value, without its quotation marks.</param>
    /// <param name="password">The <c>DPB</c> value, without its quotation marks.</param>
    /// <param name="visibility">The <c>GC</c> value, without its quotation marks.</param>
    /// <param name="encoding">Encoding of the project's single-byte text.</param>
    internal static VbaProtection Read(string? state, string? password, string? visibility, Encoding encoding)
    {
        byte[]? flags = VbaEncryption.DecryptHex(state);
        byte first = flags is { Length: > 0 } ? flags[0] : (byte)0;
        byte[]? shown = VbaEncryption.DecryptHex(visibility);

        byte[]? secret = VbaEncryption.DecryptHex(password);
        VbaPasswordHash? hash = secret is null ? null : VbaPasswordHash.Read(secret);
        return new VbaProtection(encoding, hash)
        {
            IsUserProtected = (first & 0x01) != 0,
            IsHostProtected = (first & 0x02) != 0,
            IsEditorProtected = (first & 0x04) != 0,
            IsVisible = shown is not { Length: > 0 } || shown[0] != 0x00,
            HasPassword = HasSecret(secret),
            Password = hash is null ? PlainText(secret, encoding) : null,
        };
    }

    /// <summary>A single zero byte is how a project with no password records the fact.</summary>
    /// <param name="secret">The decoded <c>DPB</c> value.</param>
    private static bool HasSecret(byte[]? secret) =>
        secret is { Length: > 0 } && (secret.Length > 1 || secret[0] != 0);

    /// <summary>
    /// The password as text, when that is what the file holds. Only a null-terminated string
    /// qualifies, and only once the bytes have failed to read as a hash.
    /// </summary>
    /// <param name="secret">The decoded <c>DPB</c> value.</param>
    /// <param name="encoding">Encoding of the project's single-byte text.</param>
    private static string? PlainText(byte[]? secret, Encoding encoding)
    {
        if (!HasSecret(secret) || secret![^1] != 0)
            return null;

        string text = encoding.GetString(secret, 0, secret.Length - 1);
        return text.Length == 0 || text.Contains('\0', StringComparison.Ordinal) ? null : text;
    }
}
