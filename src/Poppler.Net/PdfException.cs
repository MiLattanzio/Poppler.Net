namespace Poppler;

public class PdfException : Exception
{
    public PdfException(string message) : base(message)
    {
    }

    public PdfException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class PdfFormatException : PdfException
{
    public PdfFormatException(string message) : base(message)
    {
    }

    public PdfFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal PdfFormatException(string message, long offset)
        : base($"{message} (byte offset {offset}).")
    {
        Offset = offset;
    }

    public long? Offset { get; }
}

public sealed class PdfEncryptedException : PdfException
{
    public PdfEncryptedException()
        : base("The PDF is encrypted and locked. Supply the correct owner or user password.")
    {
    }
}

public sealed class PdfLimitException : PdfException
{
    public PdfLimitException(string message) : base(message)
    {
    }
}

public sealed class PdfUnsupportedFeatureException : PdfException
{
    public PdfUnsupportedFeatureException(string feature)
        : base($"The PDF feature '{feature}' is not implemented by this managed port.")
    {
        Feature = feature;
    }

    public string Feature { get; }
}
