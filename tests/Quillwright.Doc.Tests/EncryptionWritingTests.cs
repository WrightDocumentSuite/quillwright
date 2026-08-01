using System.Buffers.Binary;
using System.Text;
using Quillwright.Diagnostics;
using Quillwright.Doc.Writing;
using Quillwright.IO;
using Quillwright.Model;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Locking a document behind a password: AES for a package ([MS-OFFCRYPTO] 2.3.4.10) and RC4
/// for a legacy document (2.3.5), plus reading the oldest scheme of all (2.3.7).
/// </summary>
/// <remarks>
/// The reader and the writer are checked against each other and, where it matters, against
/// <see cref="OfficeEncryptor"/> — the test-only implementation written from the specification
/// independently of both, so that a mistake shared by the library's two halves cannot cancel
/// itself out.
/// </remarks>
public class EncryptionWritingTests
{
    private const string Password = "correct horse battery staple";

    [Fact]
    public async Task APackageSavedWithAPassword_OpensWithIt()
    {
        byte[] locked = await LockedAsync(Password);

        WordDocument opened = await WordDocument.LoadAsync(
            new MemoryStream(locked),
            new LoadOptions { Password = Password },
            TestContext.Current.CancellationToken);

        Assert.Equal("the secret text", opened.GetText());
    }

    [Fact]
    public async Task APackageSavedWithAPassword_IsNotAPackageAnyMore()
    {
        byte[] locked = await LockedAsync(Password);

        Assert.True(CompoundFile.IsCompoundFile(locked));
        Assert.True(OfficeCrypto.IsEncryptedPackage(CompoundFile.Open(locked)));

        EncryptedDocumentException error = await Assert.ThrowsAsync<EncryptedDocumentException>(
            () => WordDocument.LoadAsync(new MemoryStream(locked), cancellationToken: TestContext.Current.CancellationToken).AsTask());
        Assert.Contains("LoadOptions", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheWrongPassword_DoesNotOpenAPackageWeLocked()
    {
        byte[] locked = await LockedAsync(Password);

        await Assert.ThrowsAsync<EncryptedDocumentException>(
            () => WordDocument.LoadAsync(
                new MemoryStream(locked),
                new LoadOptions { Password = "wrong" },
                TestContext.Current.CancellationToken).AsTask());
    }

    /// <summary>
    /// The check Word makes before it even asks for the password: the encrypted package has
    /// to hash to what the description says it does ([MS-OFFCRYPTO] 2.3.4.14).
    /// </summary>
    [Fact]
    public async Task APackageWeLocked_CarriesAnIntegrityCheck()
    {
        byte[] locked = await LockedAsync(Password);
        byte[] info = CompoundFile.Open(locked).ReadStream(OfficeCrypto.InfoStream)!;

        string description = Encoding.UTF8.GetString(info, 8, info.Length - 8);
        Assert.Contains("<dataIntegrity encryptedHmacKey=\"", description, StringComparison.Ordinal);
        Assert.Contains("hashAlgorithm=\"SHA512\"", description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChangedEncryptedPackage_FailsItsAgileIntegrityCheck()
    {
        byte[] locked = await LockedAsync(Password);
        byte[] tampered = OfficeEncryptor.RewritePayload(locked, static payload =>
        {
            byte[] changed = (byte[])payload.Clone();
            changed[^1] ^= 0x01;
            return changed;
        });

        EncryptedDocumentException error = Assert.Throws<EncryptedDocumentException>(() =>
            OfficeCrypto.DecryptPackage(CompoundFile.Open(tampered), Password));

        Assert.Contains("integrity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A big enough document to need more than one of the segments the scheme chains.</summary>
    [Fact]
    public async Task APackageLargerThanOneSegment_StillOpens()
    {
        WordDocument document = WordDocument.Create();
        for (int i = 0; i < 2000; i++)
            document.Sections[0].AddParagraph($"Paragraph number {i} of a document long enough to chain.");

        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, new SaveOptions { Password = Password }, TestContext.Current.CancellationToken);
        buffer.Position = 0;

        WordDocument opened = await WordDocument.LoadAsync(
            buffer, new LoadOptions { Password = Password }, TestContext.Current.CancellationToken);

        Assert.Equal(2000, opened.Sections[0].Blocks.Paragraphs.Count());
        Assert.Contains("Paragraph number 1999", opened.GetText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The two halves checked against a third party: a package this library locked and one
    /// the test-only encryptor locked have to be readable the same way, which they can only
    /// be if both match the specification rather than each other.
    /// </summary>
    [Fact]
    public async Task WhatWeLockAndWhatTheSpecificationLocks_ReadTheSame()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("the secret text");

        var plain = new MemoryStream();
        await document.SaveAsync(plain, cancellationToken: TestContext.Current.CancellationToken);

        byte[] theirs = OfficeEncryptor.Agile(plain.ToArray(), Password);
        byte[] ours = await LockedAsync(Password);

        Assert.Equal(await ReadAsync(theirs), await ReadAsync(ours));
    }

    [Fact]
    public void ALegacyDocumentSavedWithAPassword_OpensWithIt()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("the secret text");

        byte[] locked = DocWriter.Save(document, new DocWriteOptions { Password = Password });

        Assert.Equal("the secret text", DocReader.Load(locked, Password).GetText());
    }

    [Fact]
    public void ALegacyDocumentSavedWithAPassword_SaysItIsEncrypted()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("the secret text");

        byte[] locked = DocWriter.Save(document, new DocWriteOptions { Password = Password });
        byte[] header = CompoundFile.Open(locked).ReadStream("WordDocument")!;

        Assert.Equal(0x0100, BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10)) & 0x0100);
        Assert.True(BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(14)) > 0);
        Assert.Throws<EncryptedDocumentException>(() => DocReader.Load(locked));
    }

    [Fact]
    public void TheWrongPassword_DoesNotOpenALegacyDocumentWeLocked()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("the secret text");

        byte[] locked = DocWriter.Save(document, new DocWriteOptions { Password = Password });

        Assert.Throws<EncryptedDocumentException>(() => DocReader.Load(locked, "wrong"));
    }

    /// <summary>A locked legacy document with a picture, which puts a third stream behind the lock.</summary>
    [Fact]
    public void ALockedLegacyDocument_KeepsItsDataStream()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("with a picture ");
        paragraph.AppendPicture(ImageData.FromBytes(Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==")));

        byte[] locked = DocWriter.Save(document, new DocWriteOptions { Password = Password });
        WordDocument opened = DocReader.Load(locked, Password);

        Assert.Contains("with a picture", opened.GetText(), StringComparison.Ordinal);
        Assert.Single(opened.Media);
    }

    /// <summary>
    /// The oldest scheme, which is read but never written. The fixture is built here from
    /// [MS-OFFCRYPTO] 2.3.7 rather than by the reader, so the two agree only by both matching
    /// the specification.
    /// </summary>
    [Fact]
    public void AnObfuscatedLegacyDocument_OpensWithItsPassword()
    {
        const string obfuscated = "secret";
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("scrambled but readable");

        byte[] locked = XorObfuscator.Obfuscate(DocWriter.Save(document), obfuscated);

        Assert.Equal("scrambled but readable", DocReader.Load(locked, obfuscated).GetText());
    }

    [Fact]
    public void TheWrongPassword_DoesNotOpenAnObfuscatedDocument()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("scrambled but readable");

        byte[] locked = XorObfuscator.Obfuscate(DocWriter.Save(document), "secret");

        Assert.Throws<EncryptedDocumentException>(() => DocReader.Load(locked, "wrong"));
    }

    private static async Task<string> ReadAsync(byte[] locked)
    {
        WordDocument opened = await WordDocument.LoadAsync(
            new MemoryStream(locked),
            new LoadOptions { Password = Password },
            TestContext.Current.CancellationToken);
        return opened.GetText();
    }

    private static async Task<byte[]> LockedAsync(string password)
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("the secret text");

        var buffer = new MemoryStream();
        await document.SaveAsync(
            buffer, new SaveOptions { Password = password }, TestContext.Current.CancellationToken);
        return buffer.ToArray();
    }
}
