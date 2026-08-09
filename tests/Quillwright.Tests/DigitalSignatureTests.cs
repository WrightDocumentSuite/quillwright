using System.Text;
using System.Xml.Linq;
using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// Digital signatures over a package (ECMA-376 part 2, clause 10): who signed, when, what the
/// signature covers, and whether any of it has changed since.
/// </summary>
public class DigitalSignatureTests
{
    [Fact]
    public async Task ASignedPackage_SaysWhoSignedItAndWhen()
    {
        WordDocument document = await LoadAsync(SignedPackage.Sign(await PackageAsync()));

        DigitalSignature signature = Assert.Single(document.Signatures);
        Assert.Equal(SignedPackage.Signer, signature.Signer);
        Assert.Equal(SignedPackage.SignedAt, signature.SignedAt);
        Assert.Equal(SignedPackage.Comment, signature.Comment);
        Assert.Equal("/_xmlsignatures/sig1.xml", signature.PartPath);
    }

    [Fact]
    public async Task ASignedPackage_CarriesTheSignersCertificate()
    {
        WordDocument document = await LoadAsync(SignedPackage.Sign(await PackageAsync()));

        Assert.Contains(SignedPackage.Signer, document.Signatures[0].Certificate!.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APartTheSignatureCovers_IsCheckedAgainstItsDigest()
    {
        WordDocument document = await LoadAsync(SignedPackage.Sign(await PackageAsync()));

        DigitalSignature signature = Assert.Single(document.Signatures);
        SignedPart part = Assert.Single(signature.Parts);
        Assert.Equal("/word/document.xml", part.PartPath);
        Assert.True(part.Matches);
        Assert.Equal(SignatureStatus.PartsUnchanged, signature.Status);
    }

    /// <summary>The whole point: an edit to a signed part has to be noticed.</summary>
    [Fact]
    public async Task APartEditedAfterSigning_ReportsThatItChanged()
    {
        MemoryStream package = SignedPackage.Sign(
            await PackageAsync(),
            tamper: Encoding.UTF8.GetBytes(Body("Someone else's words")));

        WordDocument document = await LoadAsync(package);

        DigitalSignature signature = Assert.Single(document.Signatures);
        Assert.Equal(SignatureStatus.PartModified, signature.Status);
        Assert.False(Assert.Single(signature.Parts).Matches);
    }

    /// <summary>
    /// A reference covering a relationships part digests a document rebuilt by the transform of
    /// clause 10.6 rather than the part itself, which used to be a reference this reader could
    /// only shrug at.
    /// </summary>
    [Fact]
    public async Task AReferenceThroughTheRelationshipTransform_IsChecked()
    {
        WordDocument document = await LoadAsync(
            SignedPackage.Sign(await PackageAsync(), alsoRelationships: true));

        DigitalSignature signature = Assert.Single(document.Signatures);
        SignedPart relationships = signature.Parts.Single(static part => part.PartPath.EndsWith(".rels", StringComparison.Ordinal));

        Assert.True(relationships.Matches);
        Assert.Equal(SignatureStatus.PartsUnchanged, signature.Status);
    }

    /// <summary>
    /// The whole point of the transform: adding a relationship to a signed part must not break
    /// the signature, because the digest is over the relationships it named and not the file.
    /// </summary>
    [Fact]
    public async Task ARelationshipAddedAfterSigning_DoesNotBreakTheSignature()
    {
        MemoryStream signed = SignedPackage.Sign(await PackageAsync(), alsoRelationships: true);

        // The transform's own document keeps only Id, Type, Target and TargetMode, so a
        // relationship written with the mode spelled out digests the same as one without it.
        MemoryStream reordered = SignedPackage.Rewrite(signed, "word/_rels/document.xml.rels", rels =>
            rels.Replace("<Relationship ", "<Relationship TargetMode=\"Internal\" ", StringComparison.Ordinal));

        WordDocument document = await LoadAsync(reordered);
        SignedPart relationships = document.Signatures[0].Parts
            .Single(static part => part.PartPath.EndsWith(".rels", StringComparison.Ordinal));

        Assert.True(relationships.Matches);
    }

    /// <summary>
    /// The mathematics, which is a different question from whether the parts are unchanged and
    /// is answered separately for that reason.
    /// </summary>
    [Fact]
    public async Task ASignatureValueOverTheSignedInfo_IsVerifiedAgainstTheSignersKey()
    {
        WordDocument document = await LoadAsync(SignedPackage.Sign(await PackageAsync()));

        DigitalSignature signature = Assert.Single(document.Signatures);
        Assert.Equal(SignatureValueStatus.Verified, signature.ValueStatus);
        Assert.True(signature.SignedInfoIntact);
    }

    [Fact]
    public async Task ASignatureUsingCanonicalisationWithComments_IsVerifiedWithItsComments()
    {
        WordDocument document = await LoadAsync(
            SignedPackage.Sign(await PackageAsync(), signedInfoComments: true));

        DigitalSignature signature = Assert.Single(document.Signatures);
        Assert.Equal(SignatureValueStatus.Verified, signature.ValueStatus);
        Assert.True(signature.SignedInfoIntact);
    }

    [Fact]
    public async Task ASignaturePartEncodedAsUtf16_IsReadAndVerified()
    {
        MemoryStream signed = SignedPackage.Sign(await PackageAsync());
        MemoryStream utf16 = SignedPackage.RewriteBytes(signed, SignedPackage.Part, ToUtf16);

        DigitalSignature signature = Assert.Single((await LoadAsync(utf16)).Signatures);
        Assert.Equal(SignatureValueStatus.Verified, signature.ValueStatus);
        Assert.Equal(SignatureStatus.PartsUnchanged, signature.Status);
    }

    [Fact]
    public async Task ARelationshipsPartEncodedAsUtf16_IsTransformedAndVerified()
    {
        MemoryStream signed = SignedPackage.Sign(await PackageAsync(), alsoRelationships: true);
        MemoryStream utf16 = SignedPackage.RewriteBytes(
            signed, "word/_rels/document.xml.rels", ToUtf16);

        DigitalSignature signature = Assert.Single((await LoadAsync(utf16)).Signatures);
        SignedPart relationships = signature.Parts.Single(
            static part => part.PartPath.EndsWith(".rels", StringComparison.Ordinal));

        Assert.True(relationships.Matches);
        Assert.Equal(SignatureStatus.PartsUnchanged, signature.Status);
    }

    [Fact]
    public async Task ASignatureValueThatDoesNotVerify_IsReportedAsInvalid()
    {
        WordDocument document = await LoadAsync(SignedPackage.Sign(await PackageAsync(), breakValue: true));

        DigitalSignature signature = Assert.Single(document.Signatures);
        Assert.Equal(SignatureValueStatus.Invalid, signature.ValueStatus);

        // The parts are untouched, so the two questions genuinely give different answers.
        Assert.Equal(SignatureStatus.PartsUnchanged, signature.Status);
    }

    /// <summary>
    /// A manifest swapped after signing leaves the value verifying over a <c>SignedInfo</c>
    /// that no longer describes what is there, which is what the inner references catch.
    /// </summary>
    [Fact]
    public async Task AManifestSwappedAfterSigning_IsCaughtByTheInnerReferences()
    {
        MemoryStream signed = SignedPackage.Sign(await PackageAsync());
        MemoryStream tampered = SignedPackage.Rewrite(signed, SignedPackage.Part, markup =>
            markup.Replace("</Manifest>", "<Reference URI=\"/none.xml\"/></Manifest>", StringComparison.Ordinal));

        DigitalSignature signature = Assert.Single((await LoadAsync(tampered)).Signatures);

        Assert.Equal(SignatureValueStatus.Verified, signature.ValueStatus);
        Assert.False(signature.SignedInfoIntact);
    }

    [Fact]
    public async Task APackageObjectWhoseReferenceDoesNotMatch_IsNotInterpreted()
    {
        MemoryStream signed = SignedPackage.Sign(await PackageAsync());
        MemoryStream tampered = SignedPackage.Rewrite(signed, SignedPackage.Part, ChangeSignatureTime);

        DigitalSignature signature = Assert.Single((await LoadAsync(tampered)).Signatures);

        Assert.Equal(SignatureValueStatus.Verified, signature.ValueStatus);
        Assert.False(signature.SignedInfoIntact);
        Assert.Empty(signature.Parts);
        Assert.Equal(SignatureStatus.Unverified, signature.Status);
    }

    [Fact]
    public async Task AnUncheckedPackageObjectReference_IsNotHiddenByALaterValidReference()
    {
        MemoryStream signed = SignedPackage.Sign(await PackageAsync());
        MemoryStream unsupported = SignedPackage.Rewrite(
            signed, SignedPackage.Part, MakePackageObjectDigestUnsupported);

        DigitalSignature signature = Assert.Single((await LoadAsync(unsupported)).Signatures);

        Assert.Null(signature.SignedInfoIntact);
        Assert.Empty(signature.Parts);
        Assert.Equal(SignatureStatus.Unverified, signature.Status);
    }

    /// <summary>
    /// A same-document URI must identify exactly one element. Otherwise a verifier can hash one
    /// object while the package reader interprets another object carrying the same identifier.
    /// </summary>
    [Theory]
    [InlineData("idPackageObject")]
    [InlineData("idOfficeObject")]
    public async Task DuplicateSameDocumentIdentifiers_AreRejectedAsAmbiguous(string identifier)
    {
        MemoryStream signed = SignedPackage.Sign(await PackageAsync());
        MemoryStream ambiguous = SignedPackage.Rewrite(
            signed, SignedPackage.Part, markup => DuplicateObjectInsideKeyInfo(markup, identifier));

        DigitalSignature signature = Assert.Single((await LoadAsync(ambiguous)).Signatures);

        // The SignedInfo bytes themselves were not changed, so their mathematical signature
        // remains a separate, truthful result. What they reference is structurally ambiguous.
        Assert.Equal(SignatureValueStatus.Verified, signature.ValueStatus);
        Assert.False(signature.SignedInfoIntact);
        Assert.Empty(signature.Parts);
        Assert.Equal(SignatureStatus.Unverified, signature.Status);
    }

    [Fact]
    public async Task SameDocumentIdentifiers_AreComparedCaseSensitively()
    {
        MemoryStream signed = SignedPackage.Sign(await PackageAsync());
        MemoryStream distinct = SignedPackage.Rewrite(
            signed,
            SignedPackage.Part,
            AddCaseDistinctIdentifierInsideKeyInfo);

        DigitalSignature signature = Assert.Single((await LoadAsync(distinct)).Signatures);

        Assert.True(signature.SignedInfoIntact);
        Assert.Equal(SignatureStatus.PartsUnchanged, signature.Status);
    }

    [Fact]
    public async Task ANestedPackageObject_IsNotAcceptedAsTheOpcPackageObject()
    {
        MemoryStream signed = SignedPackage.Sign(await PackageAsync());
        MemoryStream nested = SignedPackage.Rewrite(signed, SignedPackage.Part, MovePackageObjectInsideKeyInfo);

        DigitalSignature signature = Assert.Single((await LoadAsync(nested)).Signatures);

        Assert.Equal(SignatureValueStatus.Verified, signature.ValueStatus);
        Assert.False(signature.SignedInfoIntact);
        Assert.Empty(signature.Parts);
        Assert.Equal(SignatureStatus.Unverified, signature.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ThePackageObject_MustHaveExactlyOneSignedInfoReference(int referenceCount)
    {
        MemoryStream signed = SignedPackage.Sign(await PackageAsync());
        MemoryStream malformed = SignedPackage.Rewrite(
            signed, SignedPackage.Part, markup => SetPackageObjectReferenceCount(markup, referenceCount));

        DigitalSignature signature = Assert.Single((await LoadAsync(malformed)).Signatures);

        Assert.False(signature.SignedInfoIntact);
        Assert.Empty(signature.Parts);
        Assert.Equal(SignatureStatus.Unverified, signature.Status);
    }

    [Fact]
    public async Task ThePackageObject_MustHaveExactlyOneDirectManifest()
    {
        MemoryStream signed = SignedPackage.Sign(await PackageAsync());
        MemoryStream malformed = SignedPackage.Rewrite(signed, SignedPackage.Part, DuplicatePackageManifest);

        DigitalSignature signature = Assert.Single((await LoadAsync(malformed)).Signatures);

        Assert.Equal(SignatureValueStatus.Verified, signature.ValueStatus);
        Assert.False(signature.SignedInfoIntact);
        Assert.Empty(signature.Parts);
        Assert.Equal(SignatureStatus.Unverified, signature.Status);
    }

    [Fact]
    public async Task AnAlgorithmThisVersionDoesNotPerform_LeavesTheValueUnchecked()
    {
        MemoryStream signed = SignedPackage.Sign(await PackageAsync());
        MemoryStream exotic = SignedPackage.Rewrite(signed, SignedPackage.Part, markup =>
            markup.Replace(
                "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256",
                "urn:example:a-signature-method-nobody-has",
                StringComparison.Ordinal));

        DigitalSignature signature = Assert.Single((await LoadAsync(exotic)).Signatures);

        Assert.Equal(SignatureValueStatus.NotChecked, signature.ValueStatus);
    }

    /// <summary>
    /// Trust is the caller's question, asked on demand rather than answered while a file was
    /// open — and a certificate nobody has ever heard of is untrusted however good the
    /// mathematics is.
    /// </summary>
    [Fact]
    public async Task TheSignersCertificate_IsCheckedForTrustSeparately()
    {
        WordDocument document = await LoadAsync(SignedPackage.Sign(await PackageAsync()));

        CertificateTrust trust = Assert.NotNull(document.Signatures[0].CheckTrust());

        Assert.False(trust.IsTrusted);
        Assert.False(trust.IsExpired);
        Assert.Equal(SignatureValueStatus.Verified, document.Signatures[0].ValueStatus);
    }

    /// <summary>Signature parts are preserved, so reading a signed document does not break it.</summary>
    [Fact]
    public async Task ResavingASignedPackage_KeepsTheSignatureParts()
    {
        WordDocument document = await LoadAsync(SignedPackage.Sign(await PackageAsync()));

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        WordDocument reopened = await LoadAsync(saved);

        Assert.Single(reopened.Signatures);
        Assert.Equal(SignedPackage.Signer, reopened.Signatures[0].Signer);
    }

    [Fact]
    public async Task AnUnsignedPackage_HasNoSignatures()
    {
        WordDocument document = await LoadAsync(await PackageAsync());

        Assert.Empty(document.Signatures);
    }

    private static async Task<MemoryStream> PackageAsync()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendText("Signed content");
        return await DocumentFixture.SaveAsync(document);
    }

    private static ValueTask<WordDocument> LoadAsync(MemoryStream package)
    {
        package.Position = 0;
        return WordDocument.LoadAsync(package, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>A main document part of different bytes, for standing in after the signature.</summary>
    private static string Body(string text) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
        $"<w:body><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:body></w:document>";

    private static byte[] ToUtf16(byte[] utf8)
    {
        string markup = Encoding.UTF8.GetString(utf8)
            .Replace("encoding=\"UTF-8\"", "encoding=\"UTF-16\"", StringComparison.OrdinalIgnoreCase);

        return [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(markup)];
    }

    private static string DuplicateObjectInsideKeyInfo(
        string markup, string identifier)
    {
        XDocument document = XDocument.Parse(markup, LoadOptions.PreserveWhitespace);
        XNamespace signature = "http://www.w3.org/2000/09/xmldsig#";
        XElement root = document.Root!;
        XElement source = root.Elements(signature + "Object")
            .Single(element => element.Attribute("Id")?.Value == identifier);
        var duplicate = new XElement(source);

        root.Element(signature + "KeyInfo")!.AddFirst(duplicate);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string ChangeSignatureTime(string markup)
    {
        XDocument document = XDocument.Parse(markup, LoadOptions.PreserveWhitespace);
        XNamespace signature = "http://www.w3.org/2000/09/xmldsig#";
        XNamespace package = "http://schemas.openxmlformats.org/package/2006/digital-signature";
        XElement packageObject = document.Root!.Elements(signature + "Object")
            .Single(static element => element.Attribute("Id")?.Value == "idPackageObject");

        packageObject.Descendants(package + "Value").Single().Value = "2026-01-16T10:30:00Z";
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string MakePackageObjectDigestUnsupported(string markup)
    {
        XDocument document = XDocument.Parse(markup, LoadOptions.PreserveWhitespace);
        XNamespace signature = "http://www.w3.org/2000/09/xmldsig#";
        XElement reference = document.Root!.Element(signature + "SignedInfo")!
            .Elements(signature + "Reference")
            .Single(static element => element.Attribute("URI")?.Value == "#idPackageObject");

        reference.Element(signature + "DigestMethod")!.SetAttributeValue("Algorithm", "urn:unsupported:digest");
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string AddCaseDistinctIdentifierInsideKeyInfo(string markup)
    {
        XDocument document = XDocument.Parse(markup, LoadOptions.PreserveWhitespace);
        XNamespace signature = "http://www.w3.org/2000/09/xmldsig#";
        document.Root!.Element(signature + "KeyInfo")!.AddFirst(
            new XElement(signature + "Object", new XAttribute("Id", "IDOFFICEOBJECT")));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string MovePackageObjectInsideKeyInfo(string markup)
    {
        XDocument document = XDocument.Parse(markup, LoadOptions.PreserveWhitespace);
        XNamespace signature = "http://www.w3.org/2000/09/xmldsig#";
        XElement root = document.Root!;
        XElement packageObject = root.Elements(signature + "Object")
            .Single(static element => element.Attribute("Id")?.Value == "idPackageObject");

        packageObject.Remove();
        root.Element(signature + "KeyInfo")!.AddFirst(packageObject);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string SetPackageObjectReferenceCount(string markup, int count)
    {
        XDocument document = XDocument.Parse(markup, LoadOptions.PreserveWhitespace);
        XNamespace signature = "http://www.w3.org/2000/09/xmldsig#";
        XElement reference = document.Root!.Element(signature + "SignedInfo")!
            .Elements(signature + "Reference")
            .Single(static element => element.Attribute("URI")?.Value == "#idPackageObject");

        if (count == 0)
            reference.Remove();
        else if (count == 2)
            reference.AddAfterSelf(new XElement(reference));
        else
            throw new ArgumentOutOfRangeException(nameof(count));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string DuplicatePackageManifest(string markup)
    {
        XDocument document = XDocument.Parse(markup, LoadOptions.PreserveWhitespace);
        XNamespace signature = "http://www.w3.org/2000/09/xmldsig#";
        XElement packageObject = document.Root!.Elements(signature + "Object")
            .Single(static element => element.Attribute("Id")?.Value == "idPackageObject");
        XElement manifest = packageObject.Element(signature + "Manifest")!;

        manifest.AddAfterSelf(new XElement(manifest));
        return document.ToString(SaveOptions.DisableFormatting);
    }
}
