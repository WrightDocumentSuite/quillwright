using System.Security.Cryptography.X509Certificates;

namespace Quillwright.Model;

/// <summary>
/// Whether the bytes a signature covers are still what it covered (ECMA-376 part 2, clause 10.9).
/// </summary>
/// <remarks>
/// This answers one of the three questions a signature raises, and only one. Whether the
/// mathematics of the signature holds is <see cref="SignatureValueStatus"/>; whether the signer
/// is anybody to believe is <see cref="DigitalSignature.CheckTrust"/>.
/// </remarks>
public enum SignatureStatus : byte
{
    /// <summary>
    /// Not everything the signature covers could be checked here. It says nothing about
    /// whether the signature is good; it says this reader cannot tell.
    /// </summary>
    Unverified = 0,

    /// <summary>Every part the signature covers still hashes to what the signature recorded.</summary>
    PartsUnchanged,

    /// <summary>A part the signature covers has changed since it was signed.</summary>
    PartModified,
}

/// <summary>Whether the signature value itself verifies against the signer's public key.</summary>
/// <remarks>
/// This is the cryptography and nothing else: it says the bytes of <c>SignedInfo</c> were signed
/// by the holder of the key in the certificate. It does not say the parts the signature covers
/// are unchanged — that is <see cref="SignatureStatus"/>, and the two are checked separately
/// because a document can fail either one on its own.
/// </remarks>
public enum SignatureValueStatus : byte
{
    /// <summary>
    /// The value was not checked: no certificate, or an algorithm this version does not perform.
    /// It says nothing about whether the signature is good.
    /// </summary>
    NotChecked = 0,

    /// <summary>The value verifies against the public key of the certificate the signature carries.</summary>
    Verified,

    /// <summary>The value does not verify, so the signature has been tampered with or is not one.</summary>
    Invalid,
}

/// <summary>One part a signature covers, and the digest it recorded of it.</summary>
public sealed class SignedPart
{
    /// <summary>Absolute name of the part.</summary>
    public required string PartPath { get; init; }

    /// <summary>The algorithm the digest was taken with, as the URI the signature names it by.</summary>
    public required string DigestMethod { get; init; }

    /// <summary>The digest the signature recorded, base64 as it is stored.</summary>
    public required string DigestValue { get; init; }

    /// <summary>
    /// Whether the part still digests to that value, or <see langword="null"/> when the
    /// reference names a transform or an algorithm this version does not perform.
    /// </summary>
    public bool? Matches { get; init; }
}

/// <summary>
/// What building a chain from the signer's certificate found.
/// </summary>
/// <param name="IsTrusted">Whether the chain reached a root the policy accepts.</param>
/// <param name="IsExpired">Whether the certificate was outside its validity at the moment checked.</param>
/// <param name="IsRevoked">Whether the chain reported the certificate as revoked.</param>
/// <param name="Summary">What the chain said, one problem per line, empty when it said nothing.</param>
public readonly record struct CertificateTrust(bool IsTrusted, bool IsExpired, bool IsRevoked, string Summary);

/// <summary>
/// A digital signature over the package (ECMA-376 part 2, clause 10).
/// </summary>
/// <remarks>
/// <para>
/// Reading is one way. Signature parts are copied through untouched on save, so a signed
/// document that this library only reads keeps its signatures intact; a document it edits
/// keeps the signature parts but the signature no longer matches what they cover, exactly as
/// it would if any other tool had edited it.
/// </para>
/// <para>
/// Three separate questions are answered separately, because a document can fail any one of
/// them on its own and collapsing them into a single "valid" hides which. <see cref="Status"/>
/// says whether the parts are unchanged. <see cref="ValueStatus"/> says whether the signature
/// value verifies against the key in the certificate. <see cref="CheckTrust"/> says what a
/// chain built from that certificate found, under a policy the caller supplies — a document
/// library has no business deciding whom to trust.
/// </para>
/// </remarks>
public sealed class DigitalSignature
{
    /// <summary>Absolute name of the part holding the signature.</summary>
    public required string PartPath { get; init; }

    /// <summary>Who signed, taken from the subject of the certificate.</summary>
    public string? Signer { get; init; }

    /// <summary>When the signer says they signed (<c>SignatureTime</c>, clause 10.5.15).</summary>
    public DateTimeOffset? SignedAt { get; init; }

    /// <summary>The reason the signer gave, when the signature carries one.</summary>
    public string? Comment { get; init; }

    /// <summary>The signer's certificate, when the signature carries one.</summary>
    public X509Certificate2? Certificate { get; init; }

    /// <summary>The parts the signature covers, in the order the manifest lists them.</summary>
    public IReadOnlyList<SignedPart> Parts { get; init; } = [];

    /// <summary>Whether the parts the signature covers are unchanged.</summary>
    public SignatureStatus Status { get; init; }

    /// <summary>Whether the signature value verifies against the signer's public key.</summary>
    public SignatureValueStatus ValueStatus { get; init; }

    /// <summary>
    /// Whether the references inside <c>SignedInfo</c> — the ones that tie the manifest and the
    /// signed properties to the value — still digest to what was recorded.
    /// </summary>
    /// <remarks>
    /// A signature whose value verifies but whose <c>SignedInfo</c> references do not is one
    /// where the manifest was swapped after signing, which the value alone would not catch.
    /// </remarks>
    public bool? SignedInfoIntact { get; init; }

    /// <summary>
    /// Builds a chain from the signer's certificate and reports what it found.
    /// </summary>
    /// <param name="policy">
    /// How to build it: which roots to accept, whether to check revocation, what to allow. The
    /// default policy is the platform's, which trusts the machine's root store and checks
    /// revocation online.
    /// </param>
    /// <returns>What the chain found, or <see langword="null"/> when there is no certificate.</returns>
    /// <remarks>
    /// This is deliberately a method rather than a property: it can reach the network, it
    /// depends on a store that changes underneath it, and its answer is only as good as the
    /// policy behind it. None of that belongs in something read once while a file was open.
    /// </remarks>
    public CertificateTrust? CheckTrust(X509ChainPolicy? policy = null)
    {
        if (Certificate is null)
            return null;

        using var chain = new X509Chain();
        if (policy is not null)
            chain.ChainPolicy = policy;

        bool trusted = chain.Build(Certificate);
        X509ChainStatus[] status = chain.ChainStatus;

        return new CertificateTrust(
            trusted,
            Has(status, X509ChainStatusFlags.NotTimeValid) || Has(status, X509ChainStatusFlags.CtlNotTimeValid),
            Has(status, X509ChainStatusFlags.Revoked),
            string.Join(Environment.NewLine, status.Select(static entry => entry.StatusInformation.Trim())));
    }

    private static bool Has(X509ChainStatus[] status, X509ChainStatusFlags flag) =>
        Array.Exists(status, entry => (entry.Status & flag) != 0);
}
