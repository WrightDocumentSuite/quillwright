using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Quillwright.Formats;
using Quillwright.Styles;

namespace Quillwright.Model;

/// <summary>
/// The editing restrictions a document asks a consumer to honour
/// (<c>w:documentProtection</c>, ISO/IEC 29500-1 §17.15.1.29).
/// </summary>
/// <remarks>
/// <para>
/// This is not a security feature and the standard says so: nothing is encrypted, and an
/// application is free to ignore the whole element. What it does say is what the author asked
/// for, and whether they put a password on undoing it.
/// </para>
/// <para>
/// The password is stored as a salted, repeatedly iterated hash, so it can be checked but not
/// recovered. <see cref="IsPassword"/> checks one and <see cref="SetPassword"/> stores one the
/// way Word does — SHA-512, a fresh 16-byte salt and 100 000 iterations.
/// </para>
/// </remarks>
public sealed class DocumentProtectionSettings
{
    /// <summary>What the author restricted.</summary>
    public DocumentProtection Edit { get; set; }

    /// <summary>Whether the restriction is in force rather than merely recorded.</summary>
    public bool Enforced { get; set; }

    /// <summary>Whether formatting is restricted to the styles the document allows.</summary>
    public bool FormattingRestricted { get; set; }

    /// <summary>Name of the hashing algorithm the password was stored with.</summary>
    public string? AlgorithmName { get; set; }

    /// <summary>The stored hash, base64-encoded.</summary>
    public string? HashValue { get; set; }

    /// <summary>The salt it was hashed with, base64-encoded.</summary>
    public string? SaltValue { get; set; }

    /// <summary>How many times the hash was iterated.</summary>
    public int? SpinCount { get; set; }

    /// <summary>Attributes this version does not model, kept verbatim.</summary>
    public string? Attributes { get; set; }

    /// <summary>Whether undoing the protection needs a password this version can check.</summary>
    public bool HasPassword => HashValue is { Length: > 0 } && SaltValue is { Length: > 0 } && AlgorithmName is { Length: > 0 };

    /// <summary>Whether a password is the one the document was protected with.</summary>
    /// <param name="password">The password to check.</param>
    /// <remarks>
    /// The hash is the salt and the password hashed together, then re-hashed with a
    /// little-endian counter after it as many times as the document asks for. Note the order:
    /// document protection puts the counter after the previous hash, where package encryption
    /// puts it before.
    /// </remarks>
    public bool IsPassword(string password)
    {
        if (!HasPassword || Hash(password) is not { } computed)
            return false;

        byte[] stored = Convert.FromBase64String(HashValue!);
        return CryptographicOperations.FixedTimeEquals(computed, stored);
    }

    /// <summary>Puts a password on undoing the protection.</summary>
    /// <param name="password">The password to require.</param>
    /// <param name="spinCount">How many times to iterate the hash; Word's default is 100 000.</param>
    /// <remarks>
    /// The algorithm, salt and iteration count are replaced along with the hash: a fresh
    /// 16-byte salt every time, SHA-512, and the order §17.15.1.29 prescribes — salt then
    /// password, then each round re-hashed with the little-endian round number appended.
    /// Setting the password does not flip <see cref="Enforced"/>; that stays the caller's call.
    /// </remarks>
    public void SetPassword(string password, int spinCount = 100_000)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentOutOfRangeException.ThrowIfNegative(spinCount);

        AlgorithmName = "SHA-512";
        SaltValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        SpinCount = spinCount;
        HashValue = Convert.ToBase64String(Hash(password)!);
    }

    /// <summary>Removes the password, leaving the protection checkable by nobody and undoable by anybody.</summary>
    public void ClearPassword()
    {
        AlgorithmName = null;
        SaltValue = null;
        HashValue = null;
        SpinCount = null;
    }

    /// <summary>Reads the element, or returns <see langword="null"/> when there is none.</summary>
    /// <param name="xml">The complete <c>w:documentProtection</c> element.</param>
    internal static DocumentProtectionSettings? Parse(string? xml)
    {
        if (xml is null)
            return null;

        using var reader = XmlReader.Create(new StringReader(xml), Xml.XmlDefaults.ReaderSettings);
        if (reader.MoveToContent() != XmlNodeType.Element)
            return null;

        return new DocumentProtectionSettings
        {
            Edit = OoxmlEnums.ParseDocumentProtection(Formats.XmlHelp.Attr(reader, "edit")),
            Enforced = Formats.XmlHelp.AttrBool(reader, "enforcement") ?? false,
            FormattingRestricted = Formats.XmlHelp.AttrBool(reader, "formatting") ?? false,
            AlgorithmName = Formats.XmlHelp.Attr(reader, "algorithmName"),
            HashValue = Formats.XmlHelp.Attr(reader, "hashValue"),
            SaltValue = Formats.XmlHelp.Attr(reader, "saltValue"),
            SpinCount = Formats.XmlHelp.AttrInt(reader, "spinCount"),
            Attributes = Formats.XmlHelp.CaptureAttributes(
                reader, "edit", "enforcement", "formatting", "algorithmName", "hashValue", "saltValue", "spinCount"),
        };
    }

    /// <summary>Writes the element back, keeping the attributes this version does not model.</summary>
    internal string ToXml()
    {
        var builder = new StringBuilder("<w:documentProtection");
        Attribute(builder, "edit", OoxmlEnums.Name(Edit));
        if (FormattingRestricted)
            Attribute(builder, "formatting", "1");
        Attribute(builder, "enforcement", Enforced ? "1" : "0");
        Attribute(builder, "algorithmName", AlgorithmName);
        Attribute(builder, "hashValue", HashValue);
        Attribute(builder, "saltValue", SaltValue);
        Attribute(builder, "spinCount", SpinCount?.ToString(CultureInfo.InvariantCulture));
        return builder.Append(Attributes).Append("/>").ToString();
    }

    private static void Attribute(StringBuilder builder, string name, string? value)
    {
        if (value is not null)
            builder.Append(" w:").Append(name).Append("=\"").Append(System.Security.SecurityElement.Escape(value)).Append('"');
    }

    private byte[]? Hash(string password)
    {
        using HashAlgorithm? algorithm = IO.CryptoPrimitives.Hash(AlgorithmName!);
        if (algorithm is null)
            return null;

        byte[] digest = algorithm.ComputeHash([.. Convert.FromBase64String(SaltValue!), .. Encoding.Unicode.GetBytes(password)]);
        byte[] counter = new byte[4];
        for (int i = 0; i < (SpinCount ?? 0); i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(counter, i);
            digest = algorithm.ComputeHash([.. digest, .. counter]);
        }

        return digest;
    }
}
