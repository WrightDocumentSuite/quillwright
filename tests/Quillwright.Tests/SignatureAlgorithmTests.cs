using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Quillwright.IO;

namespace Quillwright.Tests;

public class SignatureAlgorithmTests
{
    [Fact]
    public void RsaKeysShorterThan1024Bits_AreNotSupportedForOpcSignatureValidation()
    {
        using RSA key = RSA.Create(512);
        var request = new CertificateRequest(
            "CN=Weak test key", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        byte[] content = [1, 2, 3];
        byte[] value = key.SignData(content, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        SignatureAlgorithm algorithm = Assert.IsType<SignatureAlgorithm>(SignatureAlgorithm.Of(
            "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256"));

        Assert.False(algorithm.Verify(certificate, content, value));
    }
}
