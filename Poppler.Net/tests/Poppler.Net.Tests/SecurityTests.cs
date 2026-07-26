using System.Text;
using Poppler;

namespace Poppler.Net.Tests;

public sealed class SecurityTests
{
    private const string UserPassword = "user-03";
    private const string OwnerPassword = "owner-03";

    public static IEnumerable<TestCaseData> Revisions()
    {
        yield return new TestCaseData("r2-rc4-40.pdf", 2, PdfEncryptionAlgorithm.Rc4);
        yield return new TestCaseData("r3-rc4-128.pdf", 3, PdfEncryptionAlgorithm.Rc4);
        yield return new TestCaseData("r4-aes-128.pdf", 4, PdfEncryptionAlgorithm.Aes128);
        yield return new TestCaseData("r5-aes-256.pdf", 5, PdfEncryptionAlgorithm.Aes256);
        yield return new TestCaseData("r6-aes-256.pdf", 6, PdfEncryptionAlgorithm.Aes256);
    }

    [TestCaseSource(nameof(Revisions))]
    public void OpensStandardSecurityHandlerRevisionsTwoThroughSix(
        string fileName,
        int revision,
        PdfEncryptionAlgorithm algorithm)
    {
        using Document document = Document.LoadFromData(
            ReadFixture(fileName),
            userPassword: UserPassword);

        Assert.That(document.IsEncrypted, Is.True);
        Assert.That(document.IsLocked, Is.False);
        Assert.That(document.PasswordKind, Is.EqualTo(PdfPasswordKind.User));
        Assert.That(document.EncryptionInfo, Is.Not.Null);
        PdfEncryptionInfo info = document.EncryptionInfo!;
        Assert.That(info.Revision, Is.EqualTo(revision));
        Assert.That(info.StringAlgorithm, Is.EqualTo(algorithm));
        Assert.That(info.StreamAlgorithm, Is.EqualTo(algorithm));
        Assert.That(info.EmbeddedFileAlgorithm, Is.EqualTo(algorithm));
        Assert.That(document.Title, Is.EqualTo("Poppler.Net encrypted fixture"));
        Assert.That(document.Metadata, Does.Contain("Poppler.Net encrypted XMP"));
        Assert.That(
            document.CreatePage(0).Text(),
            Does.Contain("Encrypted managed PDF R2-R6"));

        Assert.That(document.EmbeddedFiles, Has.Count.EqualTo(1));
        EmbeddedFile attachment = document.EmbeddedFiles[0];
        Assert.That(attachment.Name, Is.EqualTo("secret.txt"));
        Assert.That(
            Encoding.ASCII.GetString(attachment.Data.Span),
            Is.EqualTo("encrypted attachment payload"));
    }

    [TestCaseSource(nameof(Revisions))]
    public void LockedDocumentCanBeRetriedWithoutRetainingWrongCredentials(
        string fileName,
        int revision,
        PdfEncryptionAlgorithm algorithm)
    {
        using Document document = Document.LoadFromData(ReadFixture(fileName));

        Assert.That(document.IsEncrypted, Is.True);
        Assert.That(document.IsLocked, Is.True);
        Assert.That(document.Pages, Is.EqualTo(0));
        Assert.That(document.PasswordKind, Is.EqualTo(PdfPasswordKind.None));
        Assert.That(document.EncryptionInfo?.Revision, Is.EqualTo(revision));
        Assert.That(document.EncryptionInfo?.StreamAlgorithm, Is.EqualTo(algorithm));
        Assert.That(
            (Action)(() => document.InfoKey("Title")),
            Throws.TypeOf<PdfEncryptedException>());

        Assert.That(document.Unlock("", "wrong-password"), Is.True);
        Assert.That(document.IsLocked, Is.True);
        Assert.That(document.Unlock("", UserPassword), Is.False);
        Assert.That(document.IsLocked, Is.False);
        Assert.That(document.PasswordKind, Is.EqualTo(PdfPasswordKind.User));
        Assert.That(document.Pages, Is.EqualTo(1));
        Assert.That(document.Title, Is.EqualTo("Poppler.Net encrypted fixture"));
    }

    [TestCaseSource(nameof(Revisions))]
    public void OwnerPasswordOverridesUserPermissionMask(
        string fileName,
        int revision,
        PdfEncryptionAlgorithm algorithm)
    {
        using Document document = Document.LoadFromData(
            ReadFixture(fileName),
            ownerPassword: OwnerPassword);

        Assert.That(document.IsLocked, Is.False);
        Assert.That(document.PasswordKind, Is.EqualTo(PdfPasswordKind.Owner));
        Assert.That(document.Permissions, Is.EqualTo(Permission.All));
        Assert.That(document.HasPermission(Permission.Modify), Is.True);
    }

    [TestCaseSource(nameof(Revisions))]
    public void UserPasswordExposesPdfPermissionBits(
        string fileName,
        int revision,
        PdfEncryptionAlgorithm algorithm)
    {
        using Document document = Document.LoadFromData(
            ReadFixture(fileName),
            userPassword: UserPassword);

        Assert.That(document.HasPermission(Permission.Print), Is.True);
        Assert.That(document.HasPermission(Permission.Copy), Is.True);
        Assert.That(document.HasPermission(Permission.Accessibility), Is.True);
        Assert.That(document.HasPermission(Permission.Modify), Is.False);
        Assert.That(document.HasPermission(Permission.AddNotes), Is.False);
        Assert.That(document.HasPermission(Permission.FillForms), Is.False);
        Assert.That(document.HasPermission(Permission.Assemble), Is.False);
        Assert.That(
            document.HasPermission(Permission.HighResolutionPrint),
            Is.EqualTo(revision == 2));
    }

    [Test]
    public void SupportsDifferentStringAndStreamCryptFilters()
    {
        using Document document = Document.LoadFromData(
            ReadFixture("r4-aes-128-string-identity.pdf"),
            userPassword: UserPassword);

        Assert.That(document.EncryptionInfo, Is.Not.Null);
        PdfEncryptionInfo info = document.EncryptionInfo!;
        Assert.That(info.StringAlgorithm, Is.EqualTo(PdfEncryptionAlgorithm.Identity));
        Assert.That(info.StreamAlgorithm, Is.EqualTo(PdfEncryptionAlgorithm.Aes128));
        Assert.That(document.Title, Is.EqualTo("Poppler.Net encrypted fixture"));
        Assert.That(
            document.CreatePage(0).Text(),
            Does.Contain("Encrypted managed PDF R2-R6"));
    }

    [Test]
    public void UsesEncryptMetadataFlagWhenDerivingLegacyFileKey()
    {
        using Document document = Document.LoadFromData(
            ReadFixture("r4-aes-128-unencrypted-metadata.pdf"),
            userPassword: UserPassword);

        Assert.That(document.EncryptionInfo, Is.Not.Null);
        PdfEncryptionInfo info = document.EncryptionInfo!;
        Assert.That(info.EncryptMetadata, Is.False);
        Assert.That(document.Title, Is.EqualTo("Poppler.Net encrypted fixture"));
        Assert.That(document.Metadata, Does.Contain("Poppler.Net encrypted XMP"));
        Assert.That(
            document.CreatePage(0).Text(),
            Does.Contain("Encrypted managed PDF R2-R6"));
    }

    [Test]
    public void AppliesExplicitCryptFilterBeforeContentFilters()
    {
        using Document document = Document.LoadFromData(
            ReadFixture("r4-aes-128-explicit-crypt.pdf"),
            userPassword: UserPassword);

        Assert.That(
            document.EncryptionInfo?.StreamAlgorithm,
            Is.EqualTo(PdfEncryptionAlgorithm.Aes128));
        Assert.That(
            document.CreatePage(0).Text(),
            Does.Contain("Encrypted managed PDF R2-R6"));
    }

    [Test]
    public void SelectsEmbeddedFileCryptFilterIndependently()
    {
        using Document document = Document.LoadFromData(
            ReadFixture("r4-aes-128-embedded-file-only.pdf"),
            userPassword: UserPassword);

        Assert.That(document.EncryptionInfo, Is.Not.Null);
        PdfEncryptionInfo info = document.EncryptionInfo!;
        Assert.That(info.StringAlgorithm, Is.EqualTo(PdfEncryptionAlgorithm.Identity));
        Assert.That(info.StreamAlgorithm, Is.EqualTo(PdfEncryptionAlgorithm.Identity));
        Assert.That(info.EmbeddedFileAlgorithm, Is.EqualTo(PdfEncryptionAlgorithm.Aes128));
        Assert.That(document.EmbeddedFiles, Has.Count.EqualTo(1));
        EmbeddedFile attachment = document.EmbeddedFiles[0];
        Assert.That(
            Encoding.ASCII.GetString(attachment.Data.Span),
            Is.EqualTo("encrypted attachment payload"));
    }

    [Test]
    public void ReportsTamperedAes256PermissionsBlock()
    {
        byte[] fixture = ReadFixture("r6-aes-256.pdf");
        int marker = fixture.AsSpan().IndexOf("/Perms <"u8);
        Assert.That(marker, Is.GreaterThanOrEqualTo(0));
        int firstHexDigit = marker + "/Perms <".Length;
        fixture[firstHexDigit] = fixture[firstHexDigit] == (byte)'0'
            ? (byte)'1'
            : (byte)'0';

        using Document document = Document.LoadFromData(
            fixture,
            userPassword: UserPassword);

        Assert.That(document.IsLocked, Is.False);
        Assert.That(
            document.Diagnostics.Any(diagnostic => diagnostic.Code == "security.perms"),
            Is.True);
    }

    private static byte[] ReadFixture(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
