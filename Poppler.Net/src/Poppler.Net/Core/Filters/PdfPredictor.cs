namespace Poppler.Core.Filters;

internal static class PdfPredictor
{
    public static byte[] Decode(
        byte[] source,
        PdfDictionary parameters,
        PdfDocumentCore document,
        PdfReadOptions options)
    {
        int predictor = parameters.GetValueOrNull("Predictor").AsInteger(document) ?? 1;
        if (predictor == 1)
            return source;

        int colors = parameters.GetValueOrNull("Colors").AsInteger(document) ?? 1;
        int bitsPerComponent = parameters.GetValueOrNull("BitsPerComponent").AsInteger(document) ?? 8;
        int columns = parameters.GetValueOrNull("Columns").AsInteger(document) ?? 1;
        if (colors < 1 || bitsPerComponent is < 1 or > 16 || columns < 1)
            throw new PdfFormatException("Invalid predictor parameters.");

        int rowBytes = checked((colors * columns * bitsPerComponent + 7) / 8);
        int bytesPerPixel = Math.Max(1, checked((colors * bitsPerComponent + 7) / 8));
        if (rowBytes > options.MaximumDecodedStreamBytes)
            throw new PdfLimitException("Predictor row exceeds the decoded stream limit.");

        return predictor switch
        {
            2 => DecodeTiff(source, rowBytes, bytesPerPixel),
            >= 10 and <= 15 => DecodePng(source, rowBytes, bytesPerPixel, predictor),
            _ => throw new PdfUnsupportedFeatureException($"predictor {predictor}")
        };
    }

    private static byte[] DecodeTiff(byte[] source, int rowBytes, int bytesPerPixel)
    {
        var result = source.ToArray();
        for (int row = 0; row < result.Length; row += rowBytes)
        {
            int rowEnd = Math.Min(result.Length, row + rowBytes);
            for (int index = row + bytesPerPixel; index < rowEnd; index++)
                result[index] = unchecked((byte)(result[index] + result[index - bytesPerPixel]));
        }

        return result;
    }

    private static byte[] DecodePng(byte[] source, int rowBytes, int bytesPerPixel, int predictor)
    {
        bool hasPerRowFilter = predictor == 15 || source.Length % (rowBytes + 1) == 0;
        int encodedRowSize = rowBytes + (hasPerRowFilter ? 1 : 0);
        if (encodedRowSize == 0 || source.Length % encodedRowSize != 0)
            throw new PdfFormatException("Predictor data does not contain complete rows.");

        int rows = source.Length / encodedRowSize;
        var result = new byte[checked(rows * rowBytes)];
        int sourceOffset = 0;

        for (int row = 0; row < rows; row++)
        {
            int filter = hasPerRowFilter ? source[sourceOffset++] : predictor - 10;
            if (filter is < 0 or > 4)
                throw new PdfFormatException($"Invalid PNG predictor filter {filter}.");

            int targetOffset = row * rowBytes;
            for (int column = 0; column < rowBytes; column++)
            {
                byte raw = source[sourceOffset++];
                int left = column >= bytesPerPixel ? result[targetOffset + column - bytesPerPixel] : 0;
                int up = row > 0 ? result[targetOffset - rowBytes + column] : 0;
                int upLeft = row > 0 && column >= bytesPerPixel
                    ? result[targetOffset - rowBytes + column - bytesPerPixel]
                    : 0;
                int decoded = filter switch
                {
                    0 => raw,
                    1 => raw + left,
                    2 => raw + up,
                    3 => raw + ((left + up) >> 1),
                    4 => raw + Paeth(left, up, upLeft),
                    _ => raw
                };
                result[targetOffset + column] = unchecked((byte)decoded);
            }
        }

        return result;
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        int estimate = left + up - upLeft;
        int leftDistance = Math.Abs(estimate - left);
        int upDistance = Math.Abs(estimate - up);
        int diagonalDistance = Math.Abs(estimate - upLeft);
        return leftDistance <= upDistance && leftDistance <= diagonalDistance
            ? left
            : upDistance <= diagonalDistance
                ? up
                : upLeft;
    }
}
