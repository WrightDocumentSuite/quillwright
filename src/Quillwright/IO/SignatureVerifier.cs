using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Quillwright.Model;

namespace Quillwright.IO;

/// <summary>
/// Checks the cryptography of an XML signature: that the signature value verifies against the
/// signer's public key, and that each reference still digests to what was recorded.
/// </summary>
/// <remarks>
/// The three questions a signature raises are separate and are kept separate here. Whether the
/// mathematics holds is <see cref="CheckValue"/>. Whether the bytes it covers are unchanged is
/// <see cref="CheckReferenceAsync"/>, once per reference. Whether the certificate is one to
/// trust is neither, depends on a policy that belongs to the caller, and is on
/// <see cref="DigitalSignature.CheckTrust"/>.
/// </remarks>
internal static class SignatureVerifier
{
    /// <summary>The XML-Signature namespace.</summary>
    public static readonly XNamespace Signature = "http://www.w3.org/2000/09/xmldsig#";

    /// <summary>
    /// Verifies the signature value against the signer's public key, over the canonical form of
    /// the <c>SignedInfo</c> the signature carries.
    /// </summary>
    /// <param name="signature">The <c>Signature</c> element.</param>
    /// <param name="certificate">The signer's certificate, when the signature carries one.</param>
    public static SignatureValueStatus CheckValue(XElement signature, X509Certificate2? certificate)
    {
        ArgumentNullException.ThrowIfNull(signature);

        if (certificate is null ||
            signature.Element(Signature + "SignedInfo") is not { } signedInfo ||
            signature.Element(Signature + "SignatureValue")?.Value is not { } encoded)
        {
            return SignatureValueStatus.NotChecked;
        }

        string canonicalization = signedInfo.Element(Signature + "CanonicalizationMethod")?.Attribute("Algorithm")?.Value
            ?? CanonicalXml.Inclusive;
        string? method = signedInfo.Element(Signature + "SignatureMethod")?.Attribute("Algorithm")?.Value;

        if (!CanonicalXml.Supports(canonicalization) || SignatureAlgorithm.Of(method) is not { } algorithm)
            return SignatureValueStatus.NotChecked;

        try
        {
            byte[] value = Convert.FromBase64String(encoded.Trim());
            byte[] canonical = CanonicalXml.Canonicalize(signedInfo, canonicalization);
            return algorithm.Verify(certificate, canonical, value)
                ? SignatureValueStatus.Verified
                : SignatureValueStatus.Invalid;
        }
        catch (Exception error) when (error is FormatException or CryptographicException)
        {
            return SignatureValueStatus.NotChecked;
        }
    }

    /// <summary>
    /// Checks one reference: takes what it points at, puts it through the transforms it names,
    /// and compares the digest with the one recorded.
    /// </summary>
    /// <param name="package">The open package, for a reference that names a part.</param>
    /// <param name="reference">The <c>Reference</c> element.</param>
    /// <param name="signature">The <c>Signature</c> element, for a same-document reference.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// Whether the digest matches, or <see langword="null"/> when the reference names something
    /// this cannot reproduce — which says nothing about whether the signature is good.
    /// </returns>
    public static async ValueTask<bool?> CheckReferenceAsync(
        OpcPackage package, XElement reference, XElement signature, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(reference);

        string? recorded = reference.Element(Signature + "DigestValue")?.Value?.Trim();
        string? method = reference.Element(Signature + "DigestMethod")?.Attribute("Algorithm")?.Value;
        if (recorded is null || Digest(method) is not { } algorithm)
            return null;

        using (algorithm)
        {
            Subject? subject = await ResolveAsync(package, reference, signature, cancellationToken).ConfigureAwait(false);
            if (subject is null)
                return null;

            byte[]? bytes = Apply(reference, subject.Value);
            return bytes is null ? null : Convert.ToBase64String(algorithm.ComputeHash(bytes)) == recorded;
        }
    }

    /// <summary>What a reference points at, before its transforms are applied.</summary>
    private static async ValueTask<Subject?> ResolveAsync(
        OpcPackage package, XElement reference, XElement signature, CancellationToken cancellationToken)
    {
        string uri = reference.Attribute("URI")?.Value ?? string.Empty;

        // A reference into the signature part itself names an element by its identifier.
        if (uri.StartsWith('#'))
        {
            XElement? target = signature.Document?.Descendants()
                .FirstOrDefault(element => element.Attribute("Id")?.Value == uri[1..]);

            return target is null ? null : new Subject(null, target);
        }

        int query = uri.IndexOf('?', StringComparison.Ordinal);
        string path = Uri.UnescapeDataString(query < 0 ? uri : uri[..query]);
        if (path.Length == 0 || !package.PartExists(path))
            return null;

        return new Subject(await package.ReadPartBytesAsync(path, cancellationToken).ConfigureAwait(false), null);
    }

    /// <summary>
    /// Runs a reference's transforms in order and gives back the bytes to hash, or nothing when
    /// one of them is a transform this does not perform.
    /// </summary>
    private static byte[]? Apply(XElement reference, Subject subject)
    {
        Subject current = subject;
        IEnumerable<XElement> transforms = reference.Element(Signature + "Transforms")?.Elements(Signature + "Transform") ?? [];

        foreach (XElement transform in transforms)
        {
            string algorithm = transform.Attribute("Algorithm")?.Value ?? string.Empty;
            if (algorithm == RelationshipTransform.Algorithm)
            {
                if (current.Bytes is not { } part ||
                    RelationshipTransform.Apply(part, RelationshipSelection.Of(transform))?.Root is not { } rebuilt)
                {
                    return null;
                }

                current = new Subject(null, rebuilt);
                continue;
            }

            if (!CanonicalXml.Supports(algorithm))
                return null;

            if (Element(current) is not { } element)
                return null;

            current = new Subject(CanonicalXml.Canonicalize(element, algorithm), null);
        }

        // A chain that ends holding elements rather than bytes is canonicalised the default way,
        // which is the inclusive form (XML-Signature 4.3.3.2).
        return current.Bytes ?? (current.Element is { } last ? CanonicalXml.Canonicalize(last, CanonicalXml.Inclusive) : null);
    }

    /// <summary>The subject as an element, parsing it first when it is still bytes.</summary>
    private static XElement? Element(Subject subject)
    {
        if (subject.Element is { } element)
            return element;

        try
        {
            return subject.Bytes is { } bytes
                ? CanonicalXml.Parse(bytes).Root
                : null;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static HashAlgorithm? Digest(string? method) => method switch
    {
        "http://www.w3.org/2000/09/xmldsig#sha1" => SHA1.Create(),
        "http://www.w3.org/2001/04/xmlenc#sha256" => SHA256.Create(),
        "http://www.w3.org/2001/04/xmldsig-more#sha384" => SHA384.Create(),
        "http://www.w3.org/2001/04/xmlenc#sha512" => SHA512.Create(),
        _ => null,
    };

    /// <summary>What a transform is working on: bytes, or a piece of a document.</summary>
    /// <param name="Bytes">The bytes, when that is what it is.</param>
    /// <param name="Element">The element, when that is what it is.</param>
    private readonly record struct Subject(byte[]? Bytes, XElement? Element);
}
