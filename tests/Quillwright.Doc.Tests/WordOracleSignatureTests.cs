using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Quillwright.IO;
using Quillwright.Model;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Puts a package this library signed in front of Word itself: the signature must be one Word
/// finds and counts, not merely one the library's own verifier likes.
/// </summary>
/// <remarks>
/// The certificate is self-signed and minted for the test, so Word will not call the signature
/// trusted — trust is a property of the certificate store, not of the file. What Word can
/// vouch for is the structure: a document whose signature area is malformed opens with the
/// signatures stripped or not at all.
/// </remarks>
[Trait("Category", "word-oracle")]
[SupportedOSPlatform("windows")]
public class WordOracleSignatureTests
{
    [Fact]
    public async Task ASignedPackage_ShowsItsSignatureInWord()
    {
        Assert.SkipUnless(WordOracle.Enabled, "Set QUILLWRIGHT_WORD_ORACLE=1 and install Word to run the oracle tests.");

        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Signed by machine, checked by Word.");

        string path = Path.Combine(Path.GetTempPath(), $"quillwright-oracle-{Guid.NewGuid():N}.docx");
        await document.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        using (RSA key = RSA.Create(2048))
        {
            var request = new CertificateRequest(
                "CN=Quillwright Oracle Signer", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            await DocumentSigner.SignAsync(path, certificate, cancellationToken: TestContext.Current.CancellationToken);
        }

        object count = WordOracle.Inspect(path, opened => WordOracle.Get(WordOracle.Get(opened, "Signatures")!, "Count")!);

        Assert.Equal(1, Convert.ToInt32(count, System.Globalization.CultureInfo.InvariantCulture));
    }
}
