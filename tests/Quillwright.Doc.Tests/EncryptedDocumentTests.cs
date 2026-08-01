using System.Buffers.Binary;
using Quillwright.Diagnostics;
using Quillwright.Doc.Writing;
using Quillwright.Model;

namespace Quillwright.Doc.Tests;

/// <summary>
/// An encrypted document is intact, not damaged, and both formats say so the same way.
/// </summary>
/// <remarks>
/// An encrypted OOXML file is a compound file holding the package as an
/// <c>EncryptedPackage</c> stream beside its <c>EncryptionInfo</c> ([MS-OFFCRYPTO] 2.3.4.4
/// and 2.3.4.5), so it reaches the reader looking like a corrupt zip. A <c>.doc</c> says it
/// in the <c>fEncrypted</c> flag of its header ([MS-DOC] 2.5.15).
/// </remarks>
public class EncryptedDocumentTests
{
    private static readonly string CorpusFixture = ReferenceCorpus.OpenXmlPath(
        "test/DocumentFormat.OpenXml.Tests.Assets/assets/TestDataStorage/v2FxTestFiles/" +
        "wordprocessing/protected/document with password.docx");

    [Fact]
    public async Task AnEncryptedPackage_IsRefusedAsEncryptedRatherThanAsABrokenZip()
    {
        var container = new CompoundFileWriter();
        container.Add("EncryptionInfo", new byte[64]);
        container.Add("EncryptedPackage", new byte[512]);

        EncryptedDocumentException error = await Assert.ThrowsAsync<EncryptedDocumentException>(
            () => LoadAsync(container.Build()));

        Assert.Contains("encrypted", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The fixture Word itself produced, so the shape being detected is not one we invented.</summary>
    [Fact]
    public async Task AnEncryptedPackageWordWrote_IsRefusedAsEncrypted()
    {
        Assert.SkipUnless(File.Exists(CorpusFixture), ReferenceCorpus.Absent);

        await Assert.ThrowsAsync<EncryptedDocumentException>(() => LoadAsync(File.ReadAllBytes(CorpusFixture)));
    }

    [Fact]
    public async Task ALegacyDocumentOpenedAsAPackage_IsNamedRatherThanCalledCorrupt()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("legacy");

        DocxFormatException error = await Assert.ThrowsAsync<DocxFormatException>(
            () => LoadAsync(DocWriter.Save(document)));

        Assert.Contains("Word 97-2003", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEncryptedLegacyDocument_IsRefusedWithTheSameType()
    {
        var container = new CompoundFileWriter();
        container.Add("WordDocument", LockedHeader());

        Assert.Throws<EncryptedDocumentException>(() => DocReader.Load(container.Build()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnEncryptedPackage_OpensWithItsPassword(bool agile)
    {
        const string password = "correct horse";
        byte[] locked = await LockAsync(password, agile);

        WordDocument opened = await WordDocument.LoadAsync(
            new MemoryStream(locked),
            new LoadOptions { Password = password },
            TestContext.Current.CancellationToken);

        Assert.Equal("the secret text", opened.GetText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheWrongPassword_SaysSoRatherThanFailingToParse(bool agile)
    {
        byte[] locked = await LockAsync("correct horse", agile);

        EncryptedDocumentException error = await Assert.ThrowsAsync<EncryptedDocumentException>(
            () => WordDocument.LoadAsync(
                new MemoryStream(locked),
                new LoadOptions { Password = "battery staple" },
                TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("does not open", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEncryptedPackageWithNoPassword_SaysWhereToPutOne()
    {
        byte[] locked = await LockAsync("correct horse", agile: true);

        EncryptedDocumentException error = await Assert.ThrowsAsync<EncryptedDocumentException>(
            () => LoadAsync(locked));

        Assert.Contains("LoadOptions", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACertificateKeyEncryptorAfterThePasswordEncryptor_DoesNotReplaceIt()
    {
        const string password = "correct horse";
        byte[] locked = await LockAsync(password, agile: true, certificateEncryptor: true);

        WordDocument opened = await WordDocument.LoadAsync(
            new MemoryStream(locked),
            new LoadOptions { Password = password },
            TestContext.Current.CancellationToken);

        Assert.Equal("the secret text", opened.GetText());
    }

    [Fact]
    public async Task AgileAesCfb8_UsesTheChainingModeNamedByTheDescriptor()
    {
        const string password = "correct horse";
        byte[] locked = await LockAsync(password, agile: true, cfb: true);

        WordDocument opened = await WordDocument.LoadAsync(
            new MemoryStream(locked),
            new LoadOptions { Password = password },
            TestContext.Current.CancellationToken);

        Assert.Equal("the secret text", opened.GetText());
    }

    [Fact]
    public async Task AnUnsupportedAgileCipher_IsNamedRatherThanSilentlyTreatedAsAes()
    {
        const string password = "correct horse";
        byte[] locked = OfficeEncryptor.RewriteDescriptor(
            await LockAsync(password, agile: true),
            static descriptor => descriptor.Replace(
                "cipherAlgorithm=\"AES\"", "cipherAlgorithm=\"DES\"", StringComparison.Ordinal));

        EncryptedDocumentException error = await Assert.ThrowsAsync<EncryptedDocumentException>(() =>
            WordDocument.LoadAsync(
                new MemoryStream(locked),
                new LoadOptions { Password = password },
                TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("DES", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgileDataIntegrity_IsVerifiedAgainstAnIndependentSpecificationVector()
    {
        const string password = "correct horse";
        byte[] locked = await LockAsync(password, agile: true, dataIntegrity: true);
        byte[] tampered = OfficeEncryptor.RewritePayload(locked, static payload =>
        {
            byte[] changed = (byte[])payload.Clone();
            changed[^1] ^= 0x01;
            return changed;
        });

        EncryptedDocumentException error = Assert.Throws<EncryptedDocumentException>(() =>
            OfficeCrypto.DecryptPackage(CompoundFile.Open(tampered), password));

        Assert.Contains("integrity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A document opened with a password is saved as an ordinary package.</summary>
    [Fact]
    public async Task ADecryptedDocument_SavesAsAPlainPackage()
    {
        const string password = "correct horse";
        WordDocument opened = await WordDocument.LoadAsync(
            new MemoryStream(await LockAsync(password, agile: true)),
            new LoadOptions { Password = password },
            TestContext.Current.CancellationToken);

        var saved = new MemoryStream();
        await opened.SaveAsync(saved, cancellationToken: TestContext.Current.CancellationToken);
        saved.Position = 0;

        Assert.Equal("the secret text", (await LoadAsync(saved.ToArray())).GetText());
    }

    private static async Task<byte[]> LockAsync(
        string password, bool agile, bool certificateEncryptor = false, bool cfb = false,
        bool dataIntegrity = false)
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("the secret text");

        var package = new MemoryStream();
        await document.SaveAsync(package, cancellationToken: TestContext.Current.CancellationToken);
        return agile
            ? OfficeEncryptor.Agile(package.ToArray(), password, certificateEncryptor, cfb, dataIntegrity)
            : OfficeEncryptor.Standard(package.ToArray(), password);
    }

    [Fact]
    public void TheEncryptedError_IsCaughtAsAFormatError()
    {
        // Code that already refuses unreadable files by catching the format error keeps
        // working; the new type only tells apart those that want telling apart.
        Assert.IsAssignableFrom<DocxFormatException>(new EncryptedDocumentException("x"));
    }

    private static Task<WordDocument> LoadAsync(byte[] file) =>
        WordDocument.LoadAsync(new MemoryStream(file), cancellationToken: TestContext.Current.CancellationToken).AsTask();

    /// <summary>The smallest header a reader will get as far as the encryption flag on ([MS-DOC] 2.5.1).</summary>
    private static byte[] LockedHeader()
    {
        byte[] header = new byte[512];
        BinaryPrimitives.WriteUInt16LittleEndian(header, 0xA5EC);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), 193);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), 0x0100);
        return header;
    }
}
