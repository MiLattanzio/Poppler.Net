namespace Poppler;

public enum PdfDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record PdfDiagnostic(
    PdfDiagnosticSeverity Severity,
    string Code,
    string Message,
    long? Offset = null);
