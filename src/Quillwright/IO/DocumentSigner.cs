using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Quillwright.Formats;

namespace Quillwright.IO;

/// <summary>How a document is signed.</summary>
public sealed class SigningOptions
{
    /// <summary>The digest taken of each part and of the signature itself.</summary>
    public HashAlgorithmName Digest { get; set; } = HashAlgorithmName.SHA256;

    /// <summary>When the signature says it was made; the moment of signing when unset.</summary>
    public DateTimeOffset? Time { get; set; }

    /// <summary>The signer's stated reason, shown by Word as the commitment text.</summary>
    public string? Comments { get; set; }

    /// <summary>
    /// Whether the document properties (<c>docProps</c>) are covered too. On by default,
    /// because covering less than everything should be a decision, not a surprise; Word's own
    /// signatures leave them out so that the file system touching metadata does not read as
    /// tampering.
    /// </summary>
    public bool CoverDocumentProperties { get; set; } = true;
}

/// <summary>
/// Signs a saved package (ECMA-376 part 2, clause 10): a digest of every covered part goes
/// into a manifest, the manifest into a <c>SignedInfo</c>, and the canonical form of that under
/// an XML-Signature value made with the certificate's private key.
/// </summary>
/// <remarks>
/// <para>
/// Signing works on the file rather than on a <see cref="Model.WordDocument"/>, because a
/// signature covers bytes: the package must already be its final self. Sign as the last step,
/// and re-sign after any later save — which is not a limitation but what a signature means.
/// </para>
/// <para>
/// The signature covers every part except the signature area itself (and, when
/// <see cref="SigningOptions.CoverDocumentProperties"/> is off, <c>docProps</c>). The package
/// relationships are covered through the relationship transform of 10.6.2, selecting each
/// relationship that does not point at a signature, so adding a second signature later does not
/// break the first. What Word shows — signer, time, comment — is carried the way Word carries
/// it, in the <c>SignatureInfoV1</c> object; verification of the result is
/// <see cref="Model.WordDocument.Signatures"/> on the next load, or Word itself.
/// </para>
/// </remarks>
public static class DocumentSigner
{
    private static readonly XNamespace Ds = SignatureVerifier.Signature;
    private static readonly XNamespace Mdssi = "http://schemas.openxmlformats.org/package/2006/digital-signature";
    private static readonly XNamespace Office = "http://schemas.microsoft.com/office/2006/digsig";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Pr = "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string OriginPart = "_xmlsignatures/origin.sigs";
    private const string OriginRels = "_xmlsignatures/_rels/origin.sigs.rels";
    private const string ContentTypesPart = "[Content_Types].xml";
    private const string RootRels = "_rels/.rels";
    private const string OriginContentType = "application/vnd.openxmlformats-package.digital-signature-origin";
    private const string SignatureContentType = "application/vnd.openxmlformats-package.digital-signature-xmlsignature+xml";

    /// <summary>Signs a package file in place, adding a signature beside any already there.</summary>
    /// <param name="path">The <c>.docx</c> or <c>.docm</c> to sign.</param>
    /// <param name="certificate">The signer's certificate; it must carry its private key.</param>
    /// <param name="options">How to sign, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public static async Task SignAsync(
        string path,
        X509Certificate2 certificate,
        SigningOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, useAsync: true);
        await SignAsync(stream, certificate, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Signs a package held in a stream, adding a signature beside any already there.</summary>
    /// <param name="package">A seekable, writable stream over the whole package.</param>
    /// <param name="certificate">The signer's certificate; it must carry its private key.</param>
    /// <param name="options">How to sign, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public static async Task SignAsync(
        Stream package,
        X509Certificate2 certificate,
        SigningOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(certificate);
        if (!certificate.HasPrivateKey)
            throw new CryptographicException("The certificate carries no private key, and signing is what one is for.");

        SigningOptions signing = options ?? new SigningOptions();

        using var zip = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
        cancellationToken.ThrowIfCancellationRequested();

        int number = NextSignatureNumber(zip);
        string signaturePart = $"_xmlsignatures/sig{number}.xml";

        // The bookkeeping comes first, so the covered root relationships are the final ones.
        await EnsureOriginAsync(zip, cancellationToken).ConfigureAwait(false);
        await AddSignatureRelationshipAsync(zip, number, cancellationToken).ConfigureAwait(false);
        await DeclareContentTypesAsync(zip, signaturePart, cancellationToken).ConfigureAwait(false);

        XDocument signature = await BuildSignatureAsync(zip, certificate, signing, cancellationToken).ConfigureAwait(false);
        await WriteEntryAsync(zip, signaturePart, Serialize(signature), cancellationToken).ConfigureAwait(false);
    }

    private static int NextSignatureNumber(ZipArchive zip)
    {
        int number = 1;
        while (zip.GetEntry($"_xmlsignatures/sig{number}.xml") is not null)
            number++;

        return number;
    }

    /// <summary>The signature origin: an empty part the package relationships point at.</summary>
    private static async Task EnsureOriginAsync(ZipArchive zip, CancellationToken cancellationToken)
    {
        if (zip.GetEntry(OriginPart) is null)
            await WriteEntryAsync(zip, OriginPart, [], cancellationToken).ConfigureAwait(false);

        XDocument rels = await ReadXmlAsync(zip, RootRels, cancellationToken).ConfigureAwait(false)
            ?? new XDocument(new XElement(Pr + "Relationships"));

        XElement root = rels.Root ?? new XElement(Pr + "Relationships");
        bool present = root.Elements(Pr + "Relationship")
            .Any(static r => string.Equals(r.Attribute("Type")?.Value, DocxSchema.RelSignatureOrigin, StringComparison.OrdinalIgnoreCase));

        if (!present)
        {
            root.Add(new XElement(
                Pr + "Relationship",
                new XAttribute("Id", FreeRelationshipId(root)),
                new XAttribute("Type", DocxSchema.RelSignatureOrigin),
                new XAttribute("Target", OriginPart)));
            await WriteEntryAsync(zip, RootRels, Serialize(rels), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task AddSignatureRelationshipAsync(ZipArchive zip, int number, CancellationToken cancellationToken)
    {
        XDocument rels = await ReadXmlAsync(zip, OriginRels, cancellationToken).ConfigureAwait(false)
            ?? new XDocument(new XElement(Pr + "Relationships"));

        XElement root = rels.Root!;
        root.Add(new XElement(
            Pr + "Relationship",
            new XAttribute("Id", FreeRelationshipId(root)),
            new XAttribute("Type", DocxSchema.RelSignature),
            new XAttribute("Target", $"sig{number}.xml")));

        await WriteEntryAsync(zip, OriginRels, Serialize(rels), cancellationToken).ConfigureAwait(false);
    }

    private static string FreeRelationshipId(XElement relationships)
    {
        var taken = relationships.Elements()
            .Select(static r => r.Attribute("Id")?.Value)
            .Where(static id => id is not null)
            .ToHashSet(StringComparer.Ordinal);

        int next = taken.Count + 1;
        while (taken.Contains($"rId{next}"))
            next++;

        return $"rId{next}";
    }

    private static async Task DeclareContentTypesAsync(ZipArchive zip, string signaturePart, CancellationToken cancellationToken)
    {
        XDocument types = await ReadXmlAsync(zip, ContentTypesPart, cancellationToken).ConfigureAwait(false)
            ?? throw new Diagnostics.DocxFormatException("The package has no [Content_Types].xml, so it is not a package.");

        XElement root = types.Root!;
        bool sigsDeclared = root.Elements(Ct + "Default")
            .Any(static d => string.Equals(d.Attribute("Extension")?.Value, "sigs", StringComparison.OrdinalIgnoreCase));
        if (!sigsDeclared)
        {
            root.Add(new XElement(
                Ct + "Default",
                new XAttribute("Extension", "sigs"),
                new XAttribute("ContentType", OriginContentType)));
        }

        string partName = "/" + signaturePart;
        bool overridden = root.Elements(Ct + "Override")
            .Any(o => string.Equals(o.Attribute("PartName")?.Value, partName, StringComparison.OrdinalIgnoreCase));
        if (!overridden)
        {
            root.Add(new XElement(
                Ct + "Override",
                new XAttribute("PartName", partName),
                new XAttribute("ContentType", SignatureContentType)));
        }

        await WriteEntryAsync(zip, ContentTypesPart, Serialize(types), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<XDocument> BuildSignatureAsync(
        ZipArchive zip, X509Certificate2 certificate, SigningOptions options, CancellationToken cancellationToken)
    {
        (string digestUri, HashAlgorithmName hash) = DigestMethod(options.Digest);
        ContentTypeMap contentTypes = await ContentTypesOfAsync(zip, cancellationToken).ConfigureAwait(false);

        var manifest = new XElement(Ds + "Manifest");
        foreach (ZipArchiveEntry entry in zip.Entries.OrderBy(static e => e.FullName, StringComparer.Ordinal))
        {
            if (!IsCovered(entry, options))
                continue;

            string partPath = "/" + entry.FullName;
            byte[] content = await ReadEntryAsync(entry, cancellationToken).ConfigureAwait(false);
            manifest.Add(entry.FullName == RootRels
                ? RelationshipReference(partPath, content, contentTypes, digestUri, hash)
                : PartReference(partPath, content, contentTypes, digestUri, hash));
        }

        DateTimeOffset time = (options.Time ?? DateTimeOffset.UtcNow).ToUniversalTime();
        XDocument document = SignatureSkeleton(manifest, certificate, options, time, digestUri);

        // Serialising and parsing once makes the tree provably equal to what a verifier will
        // read back; every canonical form is then taken from that tree.
        document = CanonicalXml.Parse(Serialize(document));
        FillObjectDigests(document, digestUri, hash);
        SignValue(document, certificate, hash);
        return document;
    }

    private static bool IsCovered(ZipArchiveEntry entry, SigningOptions options)
    {
        string name = entry.FullName;
        if (name.Length == 0 || name.EndsWith('/'))
            return false;

        if (name == ContentTypesPart || name.StartsWith("_xmlsignatures/", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!options.CoverDocumentProperties && name.StartsWith("docProps/", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>A plain reference: the part's bytes, digested as they are.</summary>
    private static XElement PartReference(
        string partPath, byte[] content, ContentTypeMap contentTypes, string digestUri, HashAlgorithmName hash)
    {
        return new XElement(
            Ds + "Reference",
            new XAttribute("URI", partPath + "?ContentType=" + contentTypes.GetContentType(partPath)),
            new XElement(Ds + "DigestMethod", new XAttribute("Algorithm", digestUri)),
            new XElement(Ds + "DigestValue", Convert.ToBase64String(Hash(hash, content))));
    }

    /// <summary>
    /// The package relationships, covered through the transform of 10.6.2: each relationship
    /// that does not point at a signature is selected by identifier, so a signature added later
    /// changes only what was never covered.
    /// </summary>
    private static XElement RelationshipReference(
        string partPath, byte[] content, ContentTypeMap contentTypes, string digestUri, HashAlgorithmName hash)
    {
        XDocument rels = CanonicalXml.Parse(content);
        var ids = new List<string>();
        foreach (XElement relationship in rels.Root?.Elements(Pr + "Relationship") ?? [])
        {
            string? type = relationship.Attribute("Type")?.Value;
            if (string.Equals(type, DocxSchema.RelSignatureOrigin, StringComparison.OrdinalIgnoreCase))
                continue;

            if (relationship.Attribute("Id")?.Value is { Length: > 0 } id)
                ids.Add(id);
        }

        if (ids.Count == 0)
            throw new Diagnostics.DocxFormatException("The package relationships name nothing but signatures, so there is nothing to sign.");

        var transform = new XElement(Ds + "Transform", new XAttribute("Algorithm", RelationshipTransform.Algorithm));
        foreach (string id in ids)
            transform.Add(new XElement(Mdssi + "RelationshipReference", new XAttribute("SourceId", id)));

        XDocument transformed = RelationshipTransform.Apply(content, new RelationshipSelection(ids, []))
            ?? throw new Diagnostics.DocxFormatException("The package relationships would not parse, so they cannot be signed.");
        byte[] canonical = CanonicalXml.Canonicalize(transformed.Root!, CanonicalXml.Inclusive);

        return new XElement(
            Ds + "Reference",
            new XAttribute("URI", partPath + "?ContentType=" + contentTypes.GetContentType(partPath)),
            new XElement(
                Ds + "Transforms",
                transform,
                new XElement(Ds + "Transform", new XAttribute("Algorithm", CanonicalXml.Inclusive))),
            new XElement(Ds + "DigestMethod", new XAttribute("Algorithm", digestUri)),
            new XElement(Ds + "DigestValue", Convert.ToBase64String(Hash(hash, canonical))));
    }

    private static XDocument SignatureSkeleton(
        XElement manifest,
        X509Certificate2 certificate,
        SigningOptions options,
        DateTimeOffset time,
        string digestUri)
    {
        string stamp = time.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

        var signedInfo = new XElement(
            Ds + "SignedInfo",
            new XElement(Ds + "CanonicalizationMethod", new XAttribute("Algorithm", CanonicalXml.Inclusive)),
            new XElement(Ds + "SignatureMethod", new XAttribute("Algorithm", SignatureMethod(certificate, options.Digest))),
            ObjectReference("idPackageObject", digestUri),
            ObjectReference("idOfficeObject", digestUri));

        var packageObject = new XElement(
            Ds + "Object",
            new XAttribute("Id", "idPackageObject"),
            manifest,
            new XElement(
                Ds + "SignatureProperties",
                new XElement(
                    Ds + "SignatureProperty",
                    new XAttribute("Id", "idSignatureTime"),
                    new XAttribute("Target", "#idPackageSignature"),
                    new XElement(
                        Mdssi + "SignatureTime",
                        new XAttribute(XNamespace.Xmlns + "mdssi", Mdssi.NamespaceName),
                        new XElement(Mdssi + "Format", "YYYY-MM-DDThh:mm:ssTZD"),
                        new XElement(Mdssi + "Value", stamp)))));

        var officeObject = new XElement(
            Ds + "Object",
            new XAttribute("Id", "idOfficeObject"),
            new XElement(
                Ds + "SignatureProperties",
                new XElement(
                    Ds + "SignatureProperty",
                    new XAttribute("Id", "idOfficeV1Details"),
                    new XAttribute("Target", "#idPackageSignature"),
                    new XElement(
                        Office + "SignatureInfoV1",
                        new XAttribute("xmlns", Office.NamespaceName),
                        new XElement(Office + "SetupID"),
                        new XElement(Office + "SignatureText"),
                        new XElement(Office + "SignatureImage"),
                        new XElement(Office + "SignatureComments", options.Comments ?? string.Empty),
                        new XElement(Office + "WindowsVersion"),
                        new XElement(Office + "OfficeVersion"),
                        new XElement(Office + "ApplicationVersion"),
                        new XElement(Office + "Monitors"),
                        new XElement(Office + "HorizontalResolution"),
                        new XElement(Office + "VerticalResolution"),
                        new XElement(Office + "ColorDepth"),
                        new XElement(Office + "SignatureProviderId", "{00000000-0000-0000-0000-000000000000}"),
                        new XElement(Office + "SignatureProviderUrl"),
                        new XElement(Office + "SignatureProviderDetails", "9"),
                        new XElement(Office + "SignatureType", "1")))));

        var signature = new XElement(
            Ds + "Signature",
            new XAttribute("Id", "idPackageSignature"),
            new XAttribute("xmlns", Ds.NamespaceName),
            signedInfo,
            new XElement(Ds + "SignatureValue"),
            new XElement(
                Ds + "KeyInfo",
                new XElement(
                    Ds + "X509Data",
                    new XElement(Ds + "X509Certificate", Convert.ToBase64String(certificate.RawData)))),
            packageObject,
            officeObject);

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), signature);
    }

    private static XElement ObjectReference(string id, string digestUri) => new(
        Ds + "Reference",
        new XAttribute("URI", "#" + id),
        new XAttribute("Type", Ds.NamespaceName + "Object"),
        new XElement(Ds + "DigestMethod", new XAttribute("Algorithm", digestUri)),
        new XElement(Ds + "DigestValue"));

    /// <summary>Digests each object and puts the value into the reference that names it.</summary>
    private static void FillObjectDigests(XDocument document, string digestUri, HashAlgorithmName hash)
    {
        XElement root = document.Root!;
        foreach (XElement reference in root.Element(Ds + "SignedInfo")!.Elements(Ds + "Reference"))
        {
            string id = reference.Attribute("URI")!.Value[1..];
            XElement target = root.Elements(Ds + "Object").First(o => o.Attribute("Id")?.Value == id);
            byte[] canonical = CanonicalXml.Canonicalize(target, CanonicalXml.Inclusive);
            reference.Element(Ds + "DigestValue")!.Value = Convert.ToBase64String(Hash(hash, canonical));
        }
    }

    private static void SignValue(XDocument document, X509Certificate2 certificate, HashAlgorithmName hash)
    {
        XElement signedInfo = document.Root!.Element(Ds + "SignedInfo")!;
        byte[] canonical = CanonicalXml.Canonicalize(signedInfo, CanonicalXml.Inclusive);

        byte[] value;
        if (certificate.GetRSAPrivateKey() is { } rsa)
        {
            using (rsa)
            {
                value = rsa.SignData(canonical, hash, RSASignaturePadding.Pkcs1);
            }
        }
        else if (certificate.GetECDsaPrivateKey() is { } ecdsa)
        {
            using (ecdsa)
            {
                value = ecdsa.SignData(canonical, hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
        }
        else
        {
            throw new CryptographicException("The certificate's private key is neither RSA nor an elliptic curve, so nothing here can sign with it.");
        }

        document.Root!.Element(Ds + "SignatureValue")!.Value = Convert.ToBase64String(value);
    }

    /// <summary>The method URI for the certificate's key kind and the chosen digest.</summary>
    private static string SignatureMethod(X509Certificate2 certificate, HashAlgorithmName digest)
    {
        bool rsa = certificate.GetRSAPublicKey() is not null;
        return (rsa, digest.Name) switch
        {
            (true, "SHA1") => "http://www.w3.org/2000/09/xmldsig#rsa-sha1",
            (true, "SHA256") => "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256",
            (true, "SHA384") => "http://www.w3.org/2001/04/xmldsig-more#rsa-sha384",
            (true, "SHA512") => "http://www.w3.org/2001/04/xmldsig-more#rsa-sha512",
            (false, "SHA256") => "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256",
            (false, "SHA384") => "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha384",
            (false, "SHA512") => "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha512",
            _ => throw new CryptographicException($"No signature method pairs {digest.Name} with this certificate's key."),
        };
    }

    private static (string Uri, HashAlgorithmName Hash) DigestMethod(HashAlgorithmName digest) => digest.Name switch
    {
        "SHA1" => ("http://www.w3.org/2000/09/xmldsig#sha1", digest),
        "SHA256" => ("http://www.w3.org/2001/04/xmlenc#sha256", digest),
        "SHA384" => ("http://www.w3.org/2001/04/xmldsig-more#sha384", digest),
        "SHA512" => ("http://www.w3.org/2001/04/xmlenc#sha512", digest),
        _ => throw new CryptographicException($"{digest.Name} is not a digest XML-Signature names."),
    };

    private static byte[] Hash(HashAlgorithmName name, byte[] content) => name.Name switch
    {
        "SHA1" => SHA1.HashData(content),
        "SHA384" => SHA384.HashData(content),
        "SHA512" => SHA512.HashData(content),
        _ => SHA256.HashData(content),
    };

    private static async Task<ContentTypeMap> ContentTypesOfAsync(ZipArchive zip, CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = zip.GetEntry(ContentTypesPart)
            ?? throw new Diagnostics.DocxFormatException("The package has no [Content_Types].xml, so it is not a package.");

        byte[] content = await ReadEntryAsync(entry, cancellationToken).ConfigureAwait(false);
        return ContentTypeMap.Parse(new MemoryStream(content));
    }

    private static async Task<XDocument?> ReadXmlAsync(ZipArchive zip, string entryName, CancellationToken cancellationToken)
    {
        if (zip.GetEntry(entryName) is not { } entry)
            return null;

        return CanonicalXml.Parse(await ReadEntryAsync(entry, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        // Update mode refuses the Length of an entry once anything has been written, so the
        // buffer grows instead of being sized.
        await using Stream source = entry.Open();
        var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static async Task WriteEntryAsync(
        ZipArchive zip, string entryName, byte[] content, CancellationToken cancellationToken)
    {
        zip.GetEntry(entryName)?.Delete();
        ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using Stream target = entry.Open();
        await target.WriteAsync(content, cancellationToken).ConfigureAwait(false);
    }

    private static byte[] Serialize(XDocument document)
    {
        var buffer = new MemoryStream();
        using (var writer = System.Xml.XmlWriter.Create(
            buffer, new System.Xml.XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false }))
        {
            document.Save(writer);
        }

        return buffer.ToArray();
    }
}
