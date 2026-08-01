using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Quillwright.IO;
using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// Signing a saved package, checked with the library's own verifier: the signature it writes
/// must be one its reader calls verified with every covered part intact — and must stop being
/// one the moment a covered byte changes.
/// </summary>
public class DocumentSignerTests
{
    [Fact]
    public async Task ASignedPackage_ReadsBackVerified()
    {
        using X509Certificate2 certificate = RsaCertificate();
        MemoryStream package = await BuildPackageAsync();

        await DocumentSigner.SignAsync(
            package,
            certificate,
            new SigningOptions
            {
                Comments = "Approved for release",
                Time = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero),
            },
            TestContext.Current.CancellationToken);

        package.Position = 0;
        WordDocument reloaded = await WordDocument.LoadAsync(package, cancellationToken: TestContext.Current.CancellationToken);

        DigitalSignature signature = Assert.Single(reloaded.Signatures);
        Assert.Equal(SignatureValueStatus.Verified, signature.ValueStatus);
        Assert.Equal(SignatureStatus.PartsUnchanged, signature.Status);
        Assert.True(signature.SignedInfoIntact);
        Assert.Equal("Quillwright Signing Test", signature.Signer);
        Assert.Equal("Approved for release", signature.Comment);
        Assert.Equal(new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero), signature.SignedAt);
        Assert.Contains(signature.Parts, static part => part.PartPath == "/word/document.xml");
        Assert.All(signature.Parts, static part => Assert.True(part.Matches));
    }

    [Fact]
    public async Task ChangingACoveredPart_BreaksTheSignature()
    {
        using X509Certificate2 certificate = RsaCertificate();
        MemoryStream package = await BuildPackageAsync();
        await DocumentSigner.SignAsync(package, certificate, cancellationToken: TestContext.Current.CancellationToken);

        MemoryStream tampered = SignedPackage.Rewrite(
            package, "word/document.xml", static text => text.Replace("agreement", "amendment", StringComparison.Ordinal));

        WordDocument reloaded = await WordDocument.LoadAsync(tampered, cancellationToken: TestContext.Current.CancellationToken);

        DigitalSignature signature = Assert.Single(reloaded.Signatures);
        Assert.Equal(SignatureStatus.PartModified, signature.Status);
        Assert.Contains(signature.Parts, static part => part.PartPath == "/word/document.xml" && part.Matches == false);

        // The mathematics still holds: SignedInfo was not touched, only what it covered was.
        Assert.Equal(SignatureValueStatus.Verified, signature.ValueStatus);
    }

    [Fact]
    public async Task ASecondSignature_LeavesTheFirstStanding()
    {
        using X509Certificate2 first = RsaCertificate();
        using X509Certificate2 second = RsaCertificate();
        MemoryStream package = await BuildPackageAsync();

        await DocumentSigner.SignAsync(package, first, cancellationToken: TestContext.Current.CancellationToken);
        await DocumentSigner.SignAsync(package, second, cancellationToken: TestContext.Current.CancellationToken);

        package.Position = 0;
        WordDocument reloaded = await WordDocument.LoadAsync(package, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, reloaded.Signatures.Count);
        Assert.All(reloaded.Signatures, static signature =>
        {
            Assert.Equal(SignatureValueStatus.Verified, signature.ValueStatus);
            Assert.Equal(SignatureStatus.PartsUnchanged, signature.Status);
        });
    }

    [Fact]
    public async Task AnEllipticCurveKey_SignsToo()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Quillwright Signing Test", key, HashAlgorithmName.SHA256);
        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        MemoryStream package = await BuildPackageAsync();
        await DocumentSigner.SignAsync(package, certificate, cancellationToken: TestContext.Current.CancellationToken);

        package.Position = 0;
        WordDocument reloaded = await WordDocument.LoadAsync(package, cancellationToken: TestContext.Current.CancellationToken);

        DigitalSignature signature = Assert.Single(reloaded.Signatures);
        Assert.Equal(SignatureValueStatus.Verified, signature.ValueStatus);
        Assert.Equal(SignatureStatus.PartsUnchanged, signature.Status);
    }

    [Fact]
    public async Task DocumentProperties_CanBeLeftUncovered()
    {
        using X509Certificate2 certificate = RsaCertificate();
        MemoryStream package = await BuildPackageAsync();

        await DocumentSigner.SignAsync(
            package,
            certificate,
            new SigningOptions { CoverDocumentProperties = false },
            TestContext.Current.CancellationToken);

        MemoryStream retitled = SignedPackage.Rewrite(
            package, "docProps/core.xml", static text => text.Replace("</cp:coreProperties>",
                "<cp:category>renamed later</cp:category></cp:coreProperties>", StringComparison.Ordinal));

        WordDocument reloaded = await WordDocument.LoadAsync(retitled, cancellationToken: TestContext.Current.CancellationToken);

        DigitalSignature signature = Assert.Single(reloaded.Signatures);
        Assert.Equal(SignatureStatus.PartsUnchanged, signature.Status);
        Assert.DoesNotContain(signature.Parts, static part => part.PartPath.StartsWith("/docProps", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACertificateWithoutAPrivateKey_IsRefused()
    {
        using X509Certificate2 full = RsaCertificate();
        using var publicOnly = X509CertificateLoader.LoadCertificate(full.RawData);
        MemoryStream package = await BuildPackageAsync();

        await Assert.ThrowsAsync<CryptographicException>(
            () => DocumentSigner.SignAsync(package, publicOnly, cancellationToken: TestContext.Current.CancellationToken));
    }

    private static async Task<MemoryStream> BuildPackageAsync()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Supply agreement", "Heading1");
        document.Sections[0].AddParagraph("Signed by the parties below.");
        return await DocumentFixture.SaveAsync(document);
    }

    private static X509Certificate2 RsaCertificate()
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Quillwright Signing Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
