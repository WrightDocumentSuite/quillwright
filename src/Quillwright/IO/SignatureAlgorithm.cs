using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Quillwright.IO;

/// <summary>
/// One of the signature algorithms XML-Signature names, and how to check a value against a
/// public key with it.
/// </summary>
/// <param name="Hash">Which digest the value was taken over.</param>
/// <param name="Padding">How an RSA signature is padded, or <see langword="null"/> for elliptic curve.</param>
internal readonly record struct SignatureAlgorithm(HashAlgorithmName Hash, RSASignaturePadding? Padding)
{
    /// <summary>The algorithm an URI names, or nothing when it is one this does not check.</summary>
    /// <param name="algorithm">The <c>SignatureMethod</c> the signature declares.</param>
    /// <remarks>
    /// SHA-1 is here because documents signed a decade ago used it and refusing to look at them
    /// would be worse than looking: what comes back says the value verifies, not that the
    /// algorithm behind it is one to rely on today.
    /// </remarks>
    public static SignatureAlgorithm? Of(string? algorithm) => algorithm switch
    {
        "http://www.w3.org/2000/09/xmldsig#rsa-sha1" => new(HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1),
        "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256" => new(HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
        "http://www.w3.org/2001/04/xmldsig-more#rsa-sha384" => new(HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1),
        "http://www.w3.org/2001/04/xmldsig-more#rsa-sha512" => new(HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1),
        "http://www.w3.org/2007/05/xmldsig-more#sha256-rsa-MGF1" => new(HashAlgorithmName.SHA256, RSASignaturePadding.Pss),
        "http://www.w3.org/2007/05/xmldsig-more#sha384-rsa-MGF1" => new(HashAlgorithmName.SHA384, RSASignaturePadding.Pss),
        "http://www.w3.org/2007/05/xmldsig-more#sha512-rsa-MGF1" => new(HashAlgorithmName.SHA512, RSASignaturePadding.Pss),
        "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256" => new(HashAlgorithmName.SHA256, null),
        "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha384" => new(HashAlgorithmName.SHA384, null),
        "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha512" => new(HashAlgorithmName.SHA512, null),
        _ => null,
    };

    /// <summary>Whether a signature value verifies over the given bytes.</summary>
    /// <param name="certificate">The signer's certificate, which carries the public key.</param>
    /// <param name="signed">The canonical bytes the value was taken over.</param>
    /// <param name="value">The signature value.</param>
    public bool Verify(X509Certificate2 certificate, byte[] signed, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        if (Padding is { } padding)
        {
            using RSA? rsa = certificate.GetRSAPublicKey();
            return rsa is not null && rsa.KeySize >= 1024 && rsa.VerifyData(signed, value, Hash, padding);
        }

        // XML-Signature carries an elliptic-curve signature as the two integers one after the
        // other rather than wrapped in an ASN.1 sequence.
        using ECDsa? ecdsa = certificate.GetECDsaPublicKey();
        return ecdsa is not null && ecdsa.VerifyData(signed, value, Hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}
