using System.Text;
using Poppler;

namespace Poppler.Net.Tests;

public sealed class SecurityTests
{
    private const string UserPassword = "user-03";
    private const string OwnerPassword = "owner-03";

    public static TheoryData<string, int, PdfEncryptionAlgorithm> Revisions => new()
    {
        { "r2-rc4-40.pdf", 2, PdfEncryptionAlgorithm.Rc4 },
        { "r3-rc4-128.pdf", 3, PdfEncryptionAlgorithm.Rc4 },
        { "r4-aes-128.pdf", 4, PdfEncryptionAlgorithm.Aes128 },
        { "r5-aes-256.pdf", 5, PdfEncryptionAlgorithm.Aes256 },
        { "r6-aes-256.pdf", 6, PdfEncryptionAlgorithm.Aes256 }
    };

    [Theory]
    [MemberData(nameof(Revisions))]
    public void OpensStandardSecurityHandlerRevisionsTwoThroughSix(
        string fileName,
        int revision,
        PdfEncryptionAlgorithm algorithm)
    {
        using Document document = Document.LoadFromData(
            ReadFixture(fileName),
            userPassword: UserPassword);

        Assert.True(document.IsEncrypted);
        Assert.False(document.IsLocked);
        Assert.Equal(PdfPasswordKind.User, document.PasswordKind);
        PdfEncryptionInfo info = Assert.IsType<PdfEncryptionInfo>(document.EncryptionInfo);
        Assert.Equal(revision, info.Revision);
        Assert.Equal(algorithm, info.StringAlgorithm);
        Assert.Equal(algorithm, info.StreamAlgorithm);
        Assert.Equal(algorithm, info.EmbeddedFileAlgorithm);
        Assert.Equal("Poppler.Net encrypted fixture", document.Title);
        Assert.Contains("Poppler.Net encrypted XMP", document.Metadata);
        Assert.Contains("Encrypted managed PDF R2-R6", document.CreatePage(0).Text());

        EmbeddedFile attachment = Assert.Single(document.EmbeddedFiles);
        Assert.Equal("secret.txt", attachment.Name);
        Assert.Equal(
            "encrypted attachment payload",
            Encoding.ASCII.GetString(attachment.Data.Span));
    }

    [Theory]
    [MemberData(nameof(Revisions))]
    public void LockedDocumentCanBeRetriedWithoutRetainingWrongCredentials(
        string fileName,
        int revision,
        PdfEncryptionAlgorithm algorithm)
    {
        using Document document = Document.LoadFromData(ReadFixture(fileName));

        Assert.True(document.IsEncrypted);
        Assert.True(document.IsLocked);
        Assert.Equal(0, document.Pages);
        Assert.Equal(PdfPasswordKind.None, document.PasswordKind);
        Assert.Equal(revision, document.EncryptionInfo?.Revision);
        Assert.Equal(algorithm, document.EncryptionInfo?.StreamAlgorithm);
        Assert.Throws<PdfEncryptedException>(() => document.InfoKey("Title"));

        Assert.True(document.Unlock("", "wrong-password"));
        Assert.True(document.IsLocked);
        Assert.False(document.Unlock("", UserPassword));
        Assert.False(document.IsLocked);
        Assert.Equal(PdfPasswordKind.User, document.PasswordKind);
        Assert.Equal(1, document.Pages);
        Assert.Equal("Poppler.Net encrypted fixture", document.Title);
    }

    [Theory]
    [MemberData(nameof(Revisions))]
    public void OwnerPasswordOverridesUserPermissionMask(
        string fileName,
        int revision,
        PdfEncryptionAlgorithm algorithm)
    {
        using Document document = Document.LoadFromData(
            ReadFixture(fileName),
            ownerPassword: OwnerPassword);

        Assert.False(document.IsLocked);
        Assert.Equal(PdfPasswordKind.Owner, document.PasswordKind);
        Assert.Equal(Permission.All, document.Permissions);
        Assert.True(document.HasPermission(Permission.Modify));
    }

    [Theory]
    [MemberData(nameof(Revisions))]
    public void UserPasswordExposesPdfPermissionBits(
        string fileName,
        int revision,
        PdfEncryptionAlgorithm algorithm)
    {
        using Document document = Document.LoadFromData(
            ReadFixture(fileName),
            userPassword: UserPassword);

        Assert.True(document.HasPermission(Permission.Print));
        Assert.True(document.HasPermission(Permission.Copy));
        Assert.True(document.HasPermission(Permission.Accessibility));
        Assert.False(document.HasPermission(Permission.Modify));
        Assert.False(document.HasPermission(Permission.AddNotes));
        Assert.False(document.HasPermission(Permission.FillForms));
        Assert.False(document.HasPermission(Permission.Assemble));
        Assert.Equal(
            revision == 2,
            document.HasPermission(Permission.HighResolutionPrint));
    }

    [Fact]
    public void SupportsDifferentStringAndStreamCryptFilters()
    {
        using Document document = Document.LoadFromData(
            ReadFixture("r4-aes-128-string-identity.pdf"),
            userPassword: UserPassword);

        PdfEncryptionInfo info = Assert.IsType<PdfEncryptionInfo>(document.EncryptionInfo);
        Assert.Equal(PdfEncryptionAlgorithm.Identity, info.StringAlgorithm);
        Assert.Equal(PdfEncryptionAlgorithm.Aes128, info.StreamAlgorithm);
        Assert.Equal("Poppler.Net encrypted fixture", document.Title);
        Assert.Contains("Encrypted managed PDF R2-R6", document.CreatePage(0).Text());
    }

    [Fact]
    public void UsesEncryptMetadataFlagWhenDerivingLegacyFileKey()
    {
        using Document document = Document.LoadFromData(
            ReadFixture("r4-aes-128-unencrypted-metadata.pdf"),
            userPassword: UserPassword);

        PdfEncryptionInfo info = Assert.IsType<PdfEncryptionInfo>(document.EncryptionInfo);
        Assert.False(info.EncryptMetadata);
        Assert.Equal("Poppler.Net encrypted fixture", document.Title);
        Assert.Contains("Poppler.Net encrypted XMP", document.Metadata);
        Assert.Contains("Encrypted managed PDF R2-R6", document.CreatePage(0).Text());
    }

    [Fact]
    public void AppliesExplicitCryptFilterBeforeContentFilters()
    {
        using Document document = Document.LoadFromData(
            ReadFixture("r4-aes-128-explicit-crypt.pdf"),
            userPassword: UserPassword);

        Assert.Equal(PdfEncryptionAlgorithm.Aes128, document.EncryptionInfo?.StreamAlgorithm);
        Assert.Contains("Encrypted managed PDF R2-R6", document.CreatePage(0).Text());
    }

    [Fact]
    public void SelectsEmbeddedFileCryptFilterIndependently()
    {
        using Document document = Document.LoadFromData(
            ReadFixture("r4-aes-128-embedded-file-only.pdf"),
            userPassword: UserPassword);

        PdfEncryptionInfo info = Assert.IsType<PdfEncryptionInfo>(document.EncryptionInfo);
        Assert.Equal(PdfEncryptionAlgorithm.Identity, info.StringAlgorithm);
        Assert.Equal(PdfEncryptionAlgorithm.Identity, info.StreamAlgorithm);
        Assert.Equal(PdfEncryptionAlgorithm.Aes128, info.EmbeddedFileAlgorithm);
        EmbeddedFile attachment = Assert.Single(document.EmbeddedFiles);
        Assert.Equal(
            "encrypted attachment payload",
            Encoding.ASCII.GetString(attachment.Data.Span));
    }

    [Fact]
    public void ReportsTamperedAes256PermissionsBlock()
    {
        byte[] fixture = ReadFixture("r6-aes-256.pdf");
        int marker = fixture.AsSpan().IndexOf("/Perms <"u8);
        Assert.True(marker >= 0);
        int firstHexDigit = marker + "/Perms <".Length;
        fixture[firstHexDigit] = fixture[firstHexDigit] == (byte)'0'
            ? (byte)'1'
            : (byte)'0';

        using Document document = Document.LoadFromData(
            fixture,
            userPassword: UserPassword);

        Assert.False(document.IsLocked);
        Assert.Contains(
            document.Diagnostics,
            diagnostic => diagnostic.Code == "security.perms");
    }

    private static byte[] ReadFixture(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
