using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Quillwright.IO;

namespace Quillwright.Tests;

/// <summary>
/// Signs a package the way ECMA-376 part 2 clause 10 describes, so that the reader has
/// something to read.
/// </summary>
/// <remarks>
/// <para>
/// No document in the reference corpus is signed, and a signature made by Word would carry a
/// certificate that expires. This builds one from the specification instead: an origin part, a
/// signature part, a manifest naming the parts it covers, references tying the manifest to the
/// signature value, and a self-signed certificate minted for the test.
/// </para>
/// <para>
/// The signature value is real: the <c>SignedInfo</c> is canonicalised and signed with the
/// private key, so a reader that verifies it properly gets a pass and one that does not gets a
/// fail. Canonicalisation is the library's own — proved separately against the specification in
/// <see cref="CanonicalXmlTests"/> — which is what keeps this from being a test of the fixture
/// against itself.
/// </para>
/// </remarks>
internal static class SignedPackage
{
    private const string Origin = "_xmlsignatures/origin.sigs";
    private const string SignaturePart = "_xmlsignatures/sig1.xml";

    private const string OriginType = "application/vnd.openxmlformats-package.digital-signature-origin";
    private const string SignatureType = "application/vnd.openxmlformats-package.digital-signature-xmlsignature+xml";

    private const string Sha256 = "http://www.w3.org/2001/04/xmlenc#sha256";
    private const string C14N = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";

    private static readonly XNamespace Ds = "http://www.w3.org/2000/09/xmldsig#";

    /// <summary>The moment the fixture claims to have been signed at.</summary>
    public static DateTimeOffset SignedAt { get; } = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    /// <summary>The reason the fixture's signer gave.</summary>
    public const string Comment = "Approved for release";

    /// <summary>The name the fixture's certificate is issued to.</summary>
    public const string Signer = "Quillwright Test Signer";

    /// <summary>Signs a package over one of its parts.</summary>
    /// <param name="package">The package to sign, which is left as it was.</param>
    /// <param name="signedPart">Entry name of the part the signature covers.</param>
    /// <param name="tamper">Bytes to write over that part after signing it.</param>
    /// <param name="alsoRelationships">Whether to cover a relationships part through the transform.</param>
    /// <param name="breakValue">Whether to leave a signature value that does not verify.</param>
    /// <param name="signedInfoComments">Whether comments are included in SignedInfo canonicalisation.</param>
    public static MemoryStream Sign(
        MemoryStream package,
        string signedPart = "word/document.xml",
        byte[]? tamper = null,
        bool alsoRelationships = false,
        bool breakValue = false,
        bool signedInfoComments = false)
    {
        package.Position = 0;
        using var source = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var covered = new List<Covered> { new("/" + signedPart, Digest(Read(source, signedPart)), null) };
        if (alsoRelationships)
            covered.Add(Relationships(source));

        string markup = Markup(covered, breakValue, signedInfoComments);
        var result = new MemoryStream();

        using (var target = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ZipArchiveEntry entry in source.Entries)
            {
                byte[] content = entry.FullName switch
                {
                    "_rels/.rels" => Encoding.UTF8.GetBytes(WithOrigin(Text(entry))),
                    "[Content_Types].xml" => Encoding.UTF8.GetBytes(WithSignatureTypes(Text(entry))),
                    _ when entry.FullName == signedPart && tamper is not null => tamper,
                    _ => Read(entry),
                };

                Write(target, entry.FullName, content);
            }

            Write(target, Origin, []);
            Write(target, "_xmlsignatures/_rels/origin.sigs.rels", Encoding.UTF8.GetBytes(OriginRelationships()));
            Write(target, SignaturePart, Encoding.UTF8.GetBytes(markup));
        }

        result.Position = 0;
        return result;
    }

    /// <summary>
    /// The main part's relationships, covered the way a signature covers them: through the
    /// transform of clause 10.6, which digests a rebuilt document rather than the part.
    /// </summary>
    private static Covered Relationships(ZipArchive source)
    {
        const string Path = "word/_rels/document.xml.rels";
        byte[] markup = Read(source, Path);
        string id = CanonicalXml.Parse(markup).Root!.Elements()
            .Select(static relationship => relationship.Attribute("Id")?.Value)
            .First(static value => value is not null)!;
        string transform =
            "<Transform Algorithm=\"http://schemas.openxmlformats.org/package/2006/RelationshipTransform\">" +
            "<mdssi:RelationshipReference " +
            "xmlns:mdssi=\"http://schemas.openxmlformats.org/package/2006/digital-signature\" " +
            $"SourceId=\"{id}\"/></Transform>" +
            "<Transform Algorithm=\"" + C14N + "\"/>";

        XDocument rebuilt = RelationshipTransform.Apply(markup, new RelationshipSelection([id], []))!;
        return new Covered("/" + Path, Digest(CanonicalXml.Canonicalize(rebuilt.Root!, C14N)), transform);
    }

    /// <summary>
    /// The signature itself, built in the order the digests depend on each other: the objects
    /// first, because <c>SignedInfo</c> digests them, and the value last, because it signs
    /// <c>SignedInfo</c>.
    /// </summary>
    private static string Markup(List<Covered> covered, bool breakValue, bool signedInfoComments)
    {
        string objects = PackageObject(covered) + OfficeObject();
        (string package, string office) = Digests(objects);
        string signedInfoCanonicalisation = signedInfoComments ? CanonicalXml.InclusiveWithComments : C14N;

        string signedInfo =
            "<SignedInfo>" +
            $"<CanonicalizationMethod Algorithm=\"{signedInfoCanonicalisation}\"/>" +
            "<SignatureMethod Algorithm=\"http://www.w3.org/2001/04/xmldsig-more#rsa-sha256\"/>" +
            (signedInfoComments ? "<!--covered-->" : string.Empty) +
            Reference("#idPackageObject", package, "<Transform Algorithm=\"" + C14N + "\"/>") +
            Reference("#idOfficeObject", office, "<Transform Algorithm=\"" + C14N + "\"/>") +
            "</SignedInfo>";

        using X509Certificate2 certificate = Certificate();
        using RSA key = certificate.GetRSAPrivateKey()!;

        byte[] canonical = CanonicalXml.Canonicalize(
            Find(signedInfo + objects, "SignedInfo"), signedInfoCanonicalisation);
        byte[] value = key.SignData(
            breakValue ? [.. canonical, 0] : canonical, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Signature xmlns=\"http://www.w3.org/2000/09/xmldsig#\" Id=\"idPackageSignature\">" +
            signedInfo +
            $"<SignatureValue>{Convert.ToBase64String(value)}</SignatureValue>" +
            "<KeyInfo><X509Data>" +
            $"<X509Certificate>{Convert.ToBase64String(certificate.RawData)}</X509Certificate>" +
            "</X509Data></KeyInfo>" +
            objects +
            "</Signature>";
    }

    /// <summary>The digests of the two objects, canonicalised inside the signature they sit in.</summary>
    private static (string Package, string Office) Digests(string objects) =>
        (Digest(CanonicalXml.Canonicalize(Find(objects, "Object", "idPackageObject"), C14N)),
         Digest(CanonicalXml.Canonicalize(Find(objects, "Object", "idOfficeObject"), C14N)));

    /// <summary>
    /// Finds an element inside a fragment, under the same root the real signature gives it, so
    /// that canonicalising it sees the namespaces it will see in the finished part.
    /// </summary>
    private static XElement Find(string fragment, string name, string? id = null)
    {
        XDocument document = CanonicalXml.Parse(
            "<Signature xmlns=\"http://www.w3.org/2000/09/xmldsig#\" Id=\"idPackageSignature\">" + fragment + "</Signature>");

        return document.Descendants(Ds + name).First(element => id is null || element.Attribute("Id")?.Value == id);
    }

    private static string PackageObject(List<Covered> covered)
    {
        var manifest = new StringBuilder("<Object Id=\"idPackageObject\"><Manifest>");
        foreach (Covered part in covered)
            manifest.Append(Reference($"{part.Uri}?ContentType=application/xml", part.Digest, part.Transform));

        return manifest
            .Append("</Manifest><SignatureProperties>")
            .Append("<SignatureProperty Id=\"idSignatureTime\" Target=\"#idPackageSignature\">")
            .Append("<mdssi:SignatureTime xmlns:mdssi=\"http://schemas.openxmlformats.org/package/2006/digital-signature\">")
            .Append("<mdssi:Format>YYYY-MM-DDThh:mm:ssTZD</mdssi:Format>")
            .Append($"<mdssi:Value>{SignedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}</mdssi:Value>")
            .Append("</mdssi:SignatureTime></SignatureProperty></SignatureProperties></Object>")
            .ToString();
    }

    private static string OfficeObject() =>
        "<Object Id=\"idOfficeObject\"><SignatureProperties>" +
        "<SignatureProperty Id=\"idOfficeV1Details\" Target=\"#idPackageSignature\">" +
        "<SignatureInfoV1 xmlns=\"http://schemas.microsoft.com/office/2006/digsig\">" +
        $"<SignatureComments>{Comment}</SignatureComments>" +
        "</SignatureInfoV1></SignatureProperty></SignatureProperties></Object>";

    private static string Reference(string uri, string digest, string? transforms) =>
        $"<Reference URI=\"{uri}\">" +
        (transforms is null ? string.Empty : $"<Transforms>{transforms}</Transforms>") +
        $"<DigestMethod Algorithm=\"{Sha256}\"/><DigestValue>{digest}</DigestValue></Reference>";

    private static string Digest(byte[] content) => Convert.ToBase64String(SHA256.HashData(content));

    private static string WithOrigin(string relationships) => relationships.Replace(
        "</Relationships>",
        $"<Relationship Id=\"rIdSigOrigin\" Type=\"{Formats.DocxSchema.RelSignatureOrigin}\" Target=\"{Origin}\"/></Relationships>",
        StringComparison.Ordinal);

    private static string WithSignatureTypes(string contentTypes) => contentTypes.Replace(
        "</Types>",
        $"<Override PartName=\"/{Origin}\" ContentType=\"{OriginType}\"/>" +
        $"<Override PartName=\"/{SignaturePart}\" ContentType=\"{SignatureType}\"/></Types>",
        StringComparison.Ordinal);

    private static string OriginRelationships() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        $"<Relationship Id=\"rId1\" Type=\"{Formats.DocxSchema.RelSignature}\" Target=\"sig1.xml\"/>" +
        "</Relationships>";

    /// <summary>A certificate minted for the test, valid for as long as the test runs.</summary>
    private static X509Certificate2 Certificate()
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={Signer}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    /// <summary>Rewrites one entry of a package, leaving the rest of it alone.</summary>
    /// <param name="package">The package to copy.</param>
    /// <param name="entryName">Which entry to change.</param>
    /// <param name="edit">What to make of its text.</param>
    public static MemoryStream Rewrite(MemoryStream package, string entryName, Func<string, string> edit)
        => RewriteBytes(package, entryName, content => Encoding.UTF8.GetBytes(edit(Encoding.UTF8.GetString(content))));

    /// <summary>Rewrites one entry as bytes, leaving the rest of the package alone.</summary>
    public static MemoryStream RewriteBytes(MemoryStream package, string entryName, Func<byte[], byte[]> edit)
    {
        package.Position = 0;
        var result = new MemoryStream();
        using (var source = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        using (var target = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ZipArchiveEntry entry in source.Entries)
            {
                byte[] content = Read(entry);
                Write(target, entry.FullName, entry.FullName == entryName ? edit(content) : content);
            }
        }

        result.Position = 0;
        return result;
    }

    /// <summary>Entry name of the signature part, for a test that wants to change it.</summary>
    public static string Part => SignaturePart;

    private static void Write(ZipArchive archive, string name, byte[] content)
    {
        using Stream stream = archive.CreateEntry(name).Open();
        stream.Write(content, 0, content.Length);
    }

    private static byte[] Read(ZipArchive archive, string name) =>
        Read(archive.GetEntry(name) ?? throw new InvalidOperationException($"{name} is not in the package."));

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string Text(ZipArchiveEntry entry) => Encoding.UTF8.GetString(Read(entry));

    /// <summary>One part a fixture signature covers.</summary>
    /// <param name="Uri">Its absolute name.</param>
    /// <param name="Digest">The digest recorded for it.</param>
    /// <param name="Transform">The transforms the reference names, if any.</param>
    private readonly record struct Covered(string Uri, string Digest, string? Transform);
}
