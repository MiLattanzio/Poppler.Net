using System.Text;
using Poppler.Core;

namespace Poppler.Text;

internal sealed record PdfContentOperation(string Operator, IReadOnlyList<PdfObject> Operands);

internal static class PdfContentReader
{
    public static IEnumerable<PdfContentOperation> Read(byte[] content, PdfReadOptions options)
    {
        var reader = new PdfSyntaxReader(content, 0, content.Length, options);
        var operands = new List<PdfObject>();
        while (true)
        {
            reader.SkipTrivia();
            if (reader.AtEnd)
                yield break;

            PdfObject value;
            try
            {
                value = reader.ReadObject();
            }
            catch (PdfFormatException)
            {
                yield break;
            }

            if (value is not PdfKeyword keyword)
            {
                operands.Add(value);
                continue;
            }

            if (keyword.Value == "BI")
            {
                operands.Clear();
                SkipInlineImage(content, reader);
                continue;
            }

            yield return new PdfContentOperation(keyword.Value, operands.ToArray());
            operands.Clear();
        }
    }

    private static void SkipInlineImage(byte[] content, PdfSyntaxReader reader)
    {
        while (!reader.AtEnd)
        {
            PdfObject value = reader.ReadObject();
            if (value is PdfKeyword { Value: "ID" })
                break;
        }

        int position = reader.Position;
        if (position < content.Length && IsWhiteSpace(content[position]))
            position++;
        ReadOnlySpan<byte> remaining = content.AsSpan(position);
        for (int index = 1; index + 2 < remaining.Length; index++)
        {
            if (remaining[index] == 'E' &&
                remaining[index + 1] == 'I' &&
                IsWhiteSpace(remaining[index - 1]) &&
                IsWhiteSpace(remaining[index + 2]))
            {
                reader.Position = position + index + 2;
                return;
            }
        }

        reader.Position = content.Length;
    }

    private static bool IsWhiteSpace(byte value) =>
        value is 0 or (byte)'\t' or (byte)'\n' or (byte)'\f' or (byte)'\r' or (byte)' ';
}
