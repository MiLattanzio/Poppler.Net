using System.Text;
using Poppler.Core;

namespace Poppler.Text;

internal sealed record PdfContentOperation(
    string Operator,
    IReadOnlyList<PdfObject> Operands,
    PdfDictionary? InlineImageDictionary = null,
    ReadOnlyMemory<byte> InlineImageData = default);

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
                yield return ReadInlineImage(content, reader, options);
                continue;
            }

            yield return new PdfContentOperation(keyword.Value, operands.ToArray());
            operands.Clear();
        }
    }

    private static PdfContentOperation ReadInlineImage(
        byte[] content,
        PdfSyntaxReader reader,
        PdfReadOptions options)
    {
        var entries = new Dictionary<string, PdfObject>(StringComparer.Ordinal);
        while (!reader.AtEnd)
        {
            reader.SkipTrivia();
            PdfObject key = reader.ReadObject();
            if (key is PdfKeyword { Value: "ID" })
                break;
            if (key is not PdfName name)
                throw new PdfFormatException("Inline-image dictionary contains a non-name key.");
            reader.SkipTrivia();
            if (reader.AtEnd)
                throw new PdfFormatException("Inline-image dictionary is truncated.");
            entries[name.Value] = reader.ReadObject();
        }

        int position = reader.Position;
        if (position < content.Length && IsWhiteSpace(content[position]))
            position++;
        int end = FindInlineImageEnd(
            content,
            position,
            entries,
            options.MaximumDecodedStreamBytes);
        if (end < position)
            throw new PdfFormatException("Inline image has no valid EI terminator.");
        int terminator = end;
        while (terminator < content.Length && IsWhiteSpace(content[terminator]))
            terminator++;
        if (terminator + 1 >= content.Length ||
            content[terminator] != (byte)'E' ||
            content[terminator + 1] != (byte)'I')
        {
            throw new PdfFormatException("Inline image has no valid EI terminator.");
        }

        reader.Position = terminator + 2;
        return new PdfContentOperation(
            "BI",
            Array.Empty<PdfObject>(),
            new PdfDictionary(entries),
            content.AsMemory(position, end - position));
    }

    private static int FindInlineImageEnd(
        byte[] content,
        int start,
        IReadOnlyDictionary<string, PdfObject> dictionary,
        long maximumBytes)
    {
        if (TryRawImageLength(dictionary, out int rawLength) &&
            rawLength >= 0 &&
            rawLength <= maximumBytes &&
            start <= content.Length - rawLength)
        {
            int exactEnd = start + rawLength;
            int marker = exactEnd;
            while (marker < content.Length && IsWhiteSpace(content[marker]))
                marker++;
            if (marker + 1 < content.Length &&
                content[marker] == (byte)'E' &&
                content[marker + 1] == (byte)'I' &&
                (marker + 2 == content.Length ||
                 IsDelimiterOrWhiteSpace(content[marker + 2])))
            {
                return exactEnd;
            }
        }

        long cappedBytes = Math.Clamp(maximumBytes, 0, int.MaxValue);
        int maximumEnd = (int)Math.Min(
            content.Length,
            (long)start + cappedBytes);
        for (int index = start + 1; index + 1 < maximumEnd; index++)
        {
            if (content[index] == (byte)'E' &&
                content[index + 1] == (byte)'I' &&
                IsWhiteSpace(content[index - 1]) &&
                (index + 2 == content.Length ||
                 IsDelimiterOrWhiteSpace(content[index + 2])))
            {
                int end = index - 1;
                while (end > start && IsWhiteSpace(content[end - 1]))
                    end--;
                return end;
            }
        }

        return -1;
    }

    private static bool TryRawImageLength(
        IReadOnlyDictionary<string, PdfObject> dictionary,
        out int length)
    {
        length = 0;
        if (dictionary.ContainsKey("F") || dictionary.ContainsKey("Filter"))
            return false;
        int width = Integer(dictionary, "W", "Width");
        int height = Integer(dictionary, "H", "Height");
        bool imageMask = Boolean(dictionary, "IM", "ImageMask");
        int bits = imageMask ? 1 : Integer(dictionary, "BPC", "BitsPerComponent");
        int components = imageMask
            ? 1
            : Name(dictionary, "CS", "ColorSpace") switch
            {
                "RGB" or "DeviceRGB" => 3,
                "CMYK" or "DeviceCMYK" => 4,
                "G" or "DeviceGray" => 1,
                _ => 0
            };
        if (width <= 0 ||
            height <= 0 ||
            bits is < 1 or > 16 ||
            components <= 0)
        {
            return false;
        }

        try
        {
            long rowBits = checked((long)width * components * bits);
            long bytes = checked(((rowBits + 7) / 8) * height);
            if (bytes > int.MaxValue)
                return false;
            length = (int)bytes;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static int Integer(
        IReadOnlyDictionary<string, PdfObject> dictionary,
        string abbreviated,
        string full)
    {
        PdfObject? value = dictionary.GetValueOrDefault(abbreviated) ??
                           dictionary.GetValueOrDefault(full);
        return value is PdfNumber { IsInteger: true } number &&
               number.Value is >= int.MinValue and <= int.MaxValue
            ? (int)number.Value
            : 0;
    }

    private static bool Boolean(
        IReadOnlyDictionary<string, PdfObject> dictionary,
        string abbreviated,
        string full)
    {
        PdfObject? value = dictionary.GetValueOrDefault(abbreviated) ??
                           dictionary.GetValueOrDefault(full);
        return value is PdfBoolean { Value: true };
    }

    private static string? Name(
        IReadOnlyDictionary<string, PdfObject> dictionary,
        string abbreviated,
        string full)
    {
        PdfObject? value = dictionary.GetValueOrDefault(abbreviated) ??
                           dictionary.GetValueOrDefault(full);
        return (value as PdfName)?.Value;
    }

    private static bool IsWhiteSpace(byte value) =>
        value is 0 or (byte)'\t' or (byte)'\n' or (byte)'\f' or (byte)'\r' or (byte)' ';

    private static bool IsDelimiterOrWhiteSpace(byte value) =>
        IsWhiteSpace(value) ||
        value is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or
            (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or
            (byte)'/' or (byte)'%';
}
