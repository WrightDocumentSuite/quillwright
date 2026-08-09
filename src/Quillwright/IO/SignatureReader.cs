using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Quillwright.Formats;
using Quillwright.Model;

namespace Quillwright.IO;

/// <summary>
/// Reads the digital signatures of a package (ECMA-376 part 2, clause 10).
/// </summary>
/// <remarks>
/// <para>
/// Finding them is a walk of two relationships: the package points at a signature origin part
/// (10.4.2), and the origin part points at one signature part per signature (10.4.3). Each
/// signature part is XML-Signature markup whose OPC-specific <c>Object</c> carries a manifest
/// of the parts the signature covers and the digest it took of each.
/// </para>
/// <para>
/// Reading happens while the package is still open, because checking a digest means hashing
/// the part again, and by the time a document exists the parts it models are no longer bytes.
/// </para>
/// </remarks>
internal static class SignatureReader
{
    private static readonly XNamespace Signature = SignatureVerifier.Signature;
    private static readonly XNamespace PackageSignature = "http://schemas.openxmlformats.org/package/2006/digital-signature";
    private static readonly XNamespace OfficeSignature = "http://schemas.microsoft.com/office/2006/digsig";

    /// <summary>The identifier the specification gives the one OPC-specific object (10.5.12.2).</summary>
    private const string PackageObjectId = "idPackageObject";

    /// <summary>Reads every signature the package carries, in the order the origin part lists them.</summary>
    /// <param name="package">The open package.</param>
    /// <param name="preserved">The relationships already read from it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async ValueTask<List<DigitalSignature>> ReadAsync(
        OpcPackage package, PreservedPackage preserved, CancellationToken cancellationToken)
    {
        var signatures = new List<DigitalSignature>();
        if (Origin(preserved) is not { } origin || !preserved.Relationships.TryGetValue(origin, out List<OpcRelationship>? links))
            return signatures;

        foreach (OpcRelationship relationship in links)
        {
            if (!relationship.Is(DocxSchema.RelSignature) || relationship.IsExternal)
                continue;

            string path = OpcPath.Resolve(origin, relationship.Target);
            if (!package.PartExists(path))
                continue;

            byte[] markup = await package.ReadPartBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (Root(markup) is { } root)
                signatures.Add(await ReadOneAsync(package, root, path, cancellationToken).ConfigureAwait(false));
        }

        return signatures;
    }

    /// <summary>Where the signature origin part is, when the package has one.</summary>
    private static string? Origin(PreservedPackage preserved)
    {
        if (!preserved.Relationships.TryGetValue("/", out List<OpcRelationship>? root))
            return null;

        OpcRelationship relationship = root.FirstOrDefault(static r => r.Is(DocxSchema.RelSignatureOrigin));
        return relationship.Target is null || relationship.IsExternal ? null : OpcPath.Resolve("/", relationship.Target);
    }

    /// <summary>The signature element of a part, or nothing when the part is not one.</summary>
    private static XElement? Root(byte[] markup)
    {
        try
        {
            XElement? root = CanonicalXml.Parse(markup).Root;
            return root?.Name == Signature + "Signature" ? root : null;
        }
        catch (Exception error) when (error is System.Xml.XmlException or System.Text.DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads one signature and checks it three ways: the value against the key, the references
    /// inside <c>SignedInfo</c>, and each part the manifest covers.
    /// </summary>
    private static async ValueTask<DigitalSignature> ReadOneAsync(
        OpcPackage package, XElement root, string path, CancellationToken cancellationToken)
    {
        X509Certificate2? certificate = Certificate(root);
        OpcSignatureStructure? structure = Structure(root);
        bool? packageObjectMatches = structure is not null
            ? await SignatureVerifier.CheckReferenceAsync(
                package, structure.PackageReference, structure.Identifiers, cancellationToken).ConfigureAwait(false)
            : null;

        List<SignedPart> parts = structure is not null && packageObjectMatches == true
            ? await ManifestAsync(package, structure.Manifest, structure.Identifiers, cancellationToken).ConfigureAwait(false)
            : [];

        bool modified = parts.Exists(static part => part.Matches == false);
        bool complete = parts.Count > 0 && parts.TrueForAll(static part => part.Matches == true);

        return new DigitalSignature
        {
            PartPath = path,
            Signer = certificate?.GetNameInfo(X509NameType.SimpleName, forIssuer: false),
            SignedAt = ReadTime(root.Descendants(PackageSignature + "Value").FirstOrDefault()?.Value),
            Comment = Empty(root.Descendants(OfficeSignature + "SignatureComments").FirstOrDefault()?.Value),
            Certificate = certificate,
            Parts = parts,
            Status = modified ? SignatureStatus.PartModified
                : complete ? SignatureStatus.PartsUnchanged
                : SignatureStatus.Unverified,
            ValueStatus = SignatureVerifier.CheckValue(root, certificate),
            SignedInfoIntact = structure is not null
                ? await SignedInfoAsync(
                    package,
                    structure.SignedInfo,
                    structure.PackageReference,
                    packageObjectMatches,
                    structure.Identifiers,
                    cancellationToken).ConfigureAwait(false)
                : false,
        };
    }

    /// <summary>
    /// Resolves the OPC signature structure once, through the same identifier map every
    /// same-document reference uses. Ambiguous or incomplete structures are not interpreted.
    /// </summary>
    private static OpcSignatureStructure? Structure(XElement root)
    {
        IReadOnlyDictionary<string, XElement>? identifiers = SignatureVerifier.IndexIdentifiers(root);
        XElement? signedInfo = ExactlyOne(root.Elements(Signature + "SignedInfo"));

        if (identifiers is null || signedInfo is null ||
            !identifiers.TryGetValue(PackageObjectId, out XElement? packageObject) ||
            packageObject.Name != Signature + "Object" ||
            !ReferenceEquals(packageObject.Parent, root) ||
            ExactlyOne(packageObject.Elements(Signature + "Manifest")) is not { } manifest ||
            ExactlyOne(signedInfo.Elements(Signature + "Reference").Where(
                static reference => reference.Attribute("URI")?.Value == "#" + PackageObjectId)) is not { } packageReference)
        {
            return null;
        }

        return new OpcSignatureStructure(identifiers, signedInfo, packageReference, manifest);
    }

    /// <summary>Returns the only element in a sequence, without accepting the first of many.</summary>
    private static XElement? ExactlyOne(IEnumerable<XElement> elements)
    {
        using IEnumerator<XElement> iterator = elements.GetEnumerator();
        if (!iterator.MoveNext())
            return null;

        XElement element = iterator.Current;
        return iterator.MoveNext() ? null : element;
    }

    /// <summary>
    /// Checks the references of <c>SignedInfo</c>, which tie the manifest and the signed
    /// properties to the value. Without them, swapping a manifest after signing would go
    /// unnoticed by a check of the value alone.
    /// </summary>
    private static async ValueTask<bool?> SignedInfoAsync(
        OpcPackage package,
        XElement signedInfo,
        XElement packageReference,
        bool? packageObjectMatches,
        IReadOnlyDictionary<string, XElement> identifiers,
        CancellationToken cancellationToken)
    {
        bool sawReference = false;
        bool sawUnchecked = false;
        foreach (XElement reference in signedInfo.Elements(Signature + "Reference"))
        {
            sawReference = true;
            bool? matches = ReferenceEquals(reference, packageReference)
                ? packageObjectMatches
                : await SignatureVerifier
                    .CheckReferenceAsync(package, reference, identifiers, cancellationToken).ConfigureAwait(false);

            if (matches == false)
                return false;

            sawUnchecked |= matches is null;
        }

        return sawReference && !sawUnchecked ? true : null;
    }

    /// <summary>
    /// Every part the manifest covers, with what became of its digest. A reference the checker
    /// cannot reproduce comes back with no answer rather than a wrong one.
    /// </summary>
    private static async ValueTask<List<SignedPart>> ManifestAsync(
        OpcPackage package,
        XElement manifest,
        IReadOnlyDictionary<string, XElement> identifiers,
        CancellationToken cancellationToken)
    {
        var parts = new List<SignedPart>();

        foreach (XElement reference in manifest.Elements(Signature + "Reference"))
        {
            string uri = reference.Attribute("URI")?.Value ?? string.Empty;
            int query = uri.IndexOf('?', StringComparison.Ordinal);

            parts.Add(new SignedPart
            {
                PartPath = Uri.UnescapeDataString(query < 0 ? uri : uri[..query]),
                DigestMethod = reference.Element(Signature + "DigestMethod")?.Attribute("Algorithm")?.Value ?? string.Empty,
                DigestValue = reference.Element(Signature + "DigestValue")?.Value?.Trim() ?? string.Empty,
                Matches = await SignatureVerifier
                    .CheckReferenceAsync(package, reference, identifiers, cancellationToken).ConfigureAwait(false),
            });
        }

        return parts;
    }

    /// <summary>The one unambiguous OPC structure all signature checks share.</summary>
    private sealed record OpcSignatureStructure(
        IReadOnlyDictionary<string, XElement> Identifiers,
        XElement SignedInfo,
        XElement PackageReference,
        XElement Manifest);

    private static X509Certificate2? Certificate(XElement root)
    {
        foreach (XElement element in root.Descendants(Signature + "X509Certificate"))
        {
            try
            {
                return X509CertificateLoader.LoadCertificate(Convert.FromBase64String(element.Value.Trim()));
            }
            catch (Exception error) when (error is FormatException or CryptographicException)
            {
                // A certificate that will not load leaves the signature unattributed, which is
                // better than refusing to read the rest of what it says.
            }
        }

        return null;
    }

    private static DateTimeOffset? ReadTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset moment)
            ? moment
            : null;

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
