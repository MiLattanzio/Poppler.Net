namespace Poppler;

/// <summary>The encryption primitive selected by a PDF crypt filter.</summary>
public enum PdfEncryptionAlgorithm
{
    Identity,
    Rc4,
    Aes128,
    Aes256
}

/// <summary>The credential that successfully opened an encrypted PDF.</summary>
public enum PdfPasswordKind
{
    None,
    User,
    Owner
}

/// <summary>Read-only description of a Standard Security Handler.</summary>
public sealed record PdfEncryptionInfo(
    int Version,
    int Revision,
    int KeyLengthBits,
    PdfEncryptionAlgorithm StringAlgorithm,
    PdfEncryptionAlgorithm StreamAlgorithm,
    PdfEncryptionAlgorithm EmbeddedFileAlgorithm,
    bool EncryptMetadata);
