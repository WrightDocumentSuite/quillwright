using System.Security.Cryptography;
using System.Text;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Tests;

/// <summary>
/// The editing restrictions a document records (<c>w:documentProtection</c>, ISO/IEC 29500-1
/// §17.15.1.29) and the password that guards undoing them.
/// </summary>
public class DocumentProtectionTests
{
    private const string Password = "let me in";

    [Fact]
    public async Task ProtectionSettings_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("body");
        document.Settings.Protection = new DocumentProtectionSettings
        {
            Edit = DocumentProtection.ReadOnly,
            Enforced = true,
            FormattingRestricted = true,
        };

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "document protection");

        DocumentProtectionSettings protection = Assert.IsType<DocumentProtectionSettings>(reloaded.Settings.Protection);
        Assert.Equal(DocumentProtection.ReadOnly, protection.Edit);
        Assert.True(protection.Enforced);
        Assert.True(protection.FormattingRestricted);
    }

    [Fact]
    public void TheRightPassword_IsRecognised()
    {
        DocumentProtectionSettings protection = Protected(Password);

        Assert.True(protection.HasPassword);
        Assert.True(protection.IsPassword(Password));
    }

    [Fact]
    public void TheWrongPassword_IsNot()
    {
        Assert.False(Protected(Password).IsPassword("let me out"));
    }

    [Fact]
    public void ADocumentWithNoPassword_HasNothingToCheck()
    {
        var protection = new DocumentProtectionSettings { Edit = DocumentProtection.Comments, Enforced = true };

        Assert.False(protection.HasPassword);
        Assert.False(protection.IsPassword(Password));
    }

    /// <summary>
    /// The attributes an older producer wrote alongside the modern hash have to come back:
    /// dropping them would change what a consumer that still reads them enforces.
    /// </summary>
    [Fact]
    public async Task AttributesThisVersionDoesNotModel_ComeBack()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("body");
        document.Settings.SetRaw(
            "documentProtection",
            "<w:documentProtection w:edit=\"forms\" w:enforcement=\"1\" w:cryptProviderType=\"rsaFull\"" +
            " w:cryptAlgorithmClass=\"hash\" w:cryptAlgorithmSid=\"4\" w:cryptSpinCount=\"100000\"" +
            " w:hash=\"abcd\" w:salt=\"efgh\"/>");

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "a legacy protection element");
        string written = reloaded.Settings.GetRaw("documentProtection") ?? string.Empty;

        Assert.Contains("w:cryptProviderType=\"rsaFull\"", written, StringComparison.Ordinal);
        Assert.Contains("w:cryptSpinCount=\"100000\"", written, StringComparison.Ordinal);
        Assert.Equal(DocumentProtection.Forms, reloaded.Settings.Protection?.Edit);
    }

    [Fact]
    public void APasswordSet_IsThePasswordChecked()
    {
        var protection = new DocumentProtectionSettings { Edit = DocumentProtection.ReadOnly, Enforced = true };

        protection.SetPassword(Password);

        Assert.True(protection.HasPassword);
        Assert.True(protection.IsPassword(Password));
        Assert.False(protection.IsPassword("let me out"));
        Assert.Equal("SHA-512", protection.AlgorithmName);
        Assert.Equal(100_000, protection.SpinCount);
        Assert.Equal(16, Convert.FromBase64String(protection.SaltValue!).Length);
    }

    [Fact]
    public void SettingAgain_ReplacesSaltAndHash()
    {
        var protection = new DocumentProtectionSettings();
        protection.SetPassword(Password, spinCount: 10);
        string firstSalt = protection.SaltValue!;
        string firstHash = protection.HashValue!;

        protection.SetPassword(Password, spinCount: 10);

        Assert.NotEqual(firstSalt, protection.SaltValue);
        Assert.NotEqual(firstHash, protection.HashValue);
        Assert.True(protection.IsPassword(Password));
    }

    [Fact]
    public void ClearingThePassword_LeavesTheRestrictionButNoCheck()
    {
        var protection = new DocumentProtectionSettings { Edit = DocumentProtection.Forms, Enforced = true };
        protection.SetPassword(Password);

        protection.ClearPassword();

        Assert.False(protection.HasPassword);
        Assert.False(protection.IsPassword(Password));
        Assert.Equal(DocumentProtection.Forms, protection.Edit);
        Assert.True(protection.Enforced);
    }

    [Fact]
    public async Task APasswordSet_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("body");
        var protection = new DocumentProtectionSettings { Edit = DocumentProtection.ReadOnly, Enforced = true };
        protection.SetPassword(Password, spinCount: 1000);
        document.Settings.Protection = protection;

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "a password set by the library");

        DocumentProtectionSettings persisted = Assert.IsType<DocumentProtectionSettings>(reloaded.Settings.Protection);
        Assert.True(persisted.IsPassword(Password));
        Assert.False(persisted.IsPassword("let me out"));
    }

    /// <summary>
    /// The hash is the salt and the password together, then re-hashed with the iteration
    /// number after it — the opposite order from the one package encryption uses.
    /// </summary>
    private static DocumentProtectionSettings Protected(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        const int spins = 1000;

        byte[] digest = SHA512.HashData([.. salt, .. Encoding.Unicode.GetBytes(password)]);
        for (int i = 0; i < spins; i++)
            digest = SHA512.HashData([.. digest, .. BitConverter.GetBytes(i)]);

        return new DocumentProtectionSettings
        {
            Edit = DocumentProtection.ReadOnly,
            Enforced = true,
            AlgorithmName = "SHA-512",
            SaltValue = Convert.ToBase64String(salt),
            HashValue = Convert.ToBase64String(digest),
            SpinCount = spins,
        };
    }
}
