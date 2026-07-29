using Poppler.Color;
using Poppler.Core;

namespace Poppler.Graphics;

/// <summary>Bounded decoder for PDF shading types 4 through 7.</summary>
internal static class PdfMeshShadingReader
{
    private const int PatchDivisions = 12;

    public static bool TryRead(
        PdfObject? value,
        PdfDocumentCore document,
        PdfMatrix matrix,
        out PdfMeshShadingBrush? brush)
    {
        brush = null;
        PdfStream? stream = value.AsStream(document);
        if (stream is null)
            return false;
        PdfDictionary dictionary = stream.Dictionary;
        int type = dictionary.GetValueOrNull("ShadingType").AsInteger(document) ?? 0;
        if (type is < 4 or > 7)
            return false;

        int coordinateBits =
            dictionary.GetValueOrNull("BitsPerCoordinate").AsInteger(document) ?? 0;
        int componentBits =
            dictionary.GetValueOrNull("BitsPerComponent").AsInteger(document) ?? 0;
        if (coordinateBits is < 1 or > 32 || componentBits is < 1 or > 32)
            return false;

        PdfColorSpaceDefinition? colorSpace = PdfColorSpaceDefinition.Parse(
            dictionary.GetValueOrNull("ColorSpace"),
            resources: null,
            document);
        double[]? decode = PdfFunction.ReadNumbers(
            dictionary.GetValueOrNull("Decode"),
            document);
        if (colorSpace is null || decode is null || decode.Length < 6 ||
            (decode.Length - 4) % 2 != 0)
        {
            return false;
        }

        int dataComponents = (decode.Length - 4) / 2;
        PdfObject? function = dictionary.GetValueOrNull("Function");
        if (function is null && dataComponents != colorSpace.Components)
            return false;
        if (function is not null && dataComponents != 1)
            return false;

        byte[] bytes = document.Decode(stream);
        var reader = new MeshBitReader(bytes);
        var triangles = new List<PdfMeshTriangle>();
        bool success = type switch
        {
            4 => ReadFreeForm(
                dictionary,
                document,
                reader,
                coordinateBits,
                componentBits,
                decode,
                function,
                colorSpace,
                triangles),
            5 => ReadLattice(
                dictionary,
                document,
                reader,
                coordinateBits,
                componentBits,
                decode,
                function,
                colorSpace,
                triangles),
            6 or 7 => ReadPatches(
                type,
                dictionary,
                document,
                reader,
                coordinateBits,
                componentBits,
                decode,
                function,
                colorSpace,
                triangles),
            _ => false
        };
        if (!success || triangles.Count == 0)
            return false;
        if (triangles.Count > document.Options.MaximumMeshTriangles)
            throw new PdfLimitException("Mesh shading exceeds the configured triangle limit.");

        brush = new PdfMeshShadingBrush(
            type switch
            {
                4 => PdfShadingKind.FreeFormGouraud,
                5 => PdfShadingKind.LatticeGouraud,
                6 => PdfShadingKind.CoonsPatch,
                _ => PdfShadingKind.TensorProductPatch
            },
            triangles,
            matrix);
        return true;
    }

    private static bool ReadFreeForm(
        PdfDictionary dictionary,
        PdfDocumentCore document,
        MeshBitReader reader,
        int coordinateBits,
        int componentBits,
        double[] decode,
        PdfObject? function,
        PdfColorSpaceDefinition colorSpace,
        List<PdfMeshTriangle> triangles)
    {
        int flagBits = dictionary.GetValueOrNull("BitsPerFlag").AsInteger(document) ?? 0;
        if (flagBits is < 1 or > 8)
            return false;

        var vertices = new List<PdfMeshVertex>();
        int state = 0;
        while (reader.TryRead(flagBits, out uint rawFlag) &&
               TryReadVertex(
                   reader,
                   coordinateBits,
                   componentBits,
                   decode,
                   function,
                   colorSpace,
                   document,
                   out PdfMeshVertex vertex))
        {
            reader.Align();
            int flag = (int)rawFlag;
            vertices.Add(vertex);
            if (state is 0 or 1)
            {
                state++;
                continue;
            }

            if (state == 2)
            {
                AddTriangle(
                    triangles,
                    vertices[^3],
                    vertices[^2],
                    vertices[^1],
                    document);
                state = 3;
            }
            else if (flag == 1 && triangles.Count > 0)
            {
                PdfMeshTriangle previous = triangles[^1];
                AddTriangle(
                    triangles,
                    previous.Second,
                    previous.Third,
                    vertex,
                    document);
            }
            else if (flag == 2 && triangles.Count > 0)
            {
                PdfMeshTriangle previous = triangles[^1];
                AddTriangle(
                    triangles,
                    previous.First,
                    previous.Third,
                    vertex,
                    document);
            }
            else if (flag == 0)
            {
                state = 1;
            }
            else
            {
                return false;
            }
        }

        return triangles.Count > 0;
    }

    private static bool ReadLattice(
        PdfDictionary dictionary,
        PdfDocumentCore document,
        MeshBitReader reader,
        int coordinateBits,
        int componentBits,
        double[] decode,
        PdfObject? function,
        PdfColorSpaceDefinition colorSpace,
        List<PdfMeshTriangle> triangles)
    {
        int verticesPerRow =
            dictionary.GetValueOrNull("VerticesPerRow").AsInteger(document) ?? 0;
        if (verticesPerRow < 2)
            return false;

        var vertices = new List<PdfMeshVertex>();
        while (TryReadVertex(
                   reader,
                   coordinateBits,
                   componentBits,
                   decode,
                   function,
                   colorSpace,
                   document,
                   out PdfMeshVertex vertex))
        {
            vertices.Add(vertex);
            reader.Align();
            if (vertices.Count > document.Options.MaximumCollectionItems)
                throw new PdfLimitException("Mesh vertex count exceeds the configured limit.");
        }

        int rows = vertices.Count / verticesPerRow;
        if (rows < 2)
            return false;
        for (int row = 0; row < rows - 1; row++)
        {
            for (int column = 0; column < verticesPerRow - 1; column++)
            {
                int topLeft = row * verticesPerRow + column;
                int topRight = topLeft + 1;
                int bottomLeft = topLeft + verticesPerRow;
                int bottomRight = bottomLeft + 1;
                AddTriangle(
                    triangles,
                    vertices[topLeft],
                    vertices[topRight],
                    vertices[bottomLeft],
                    document);
                AddTriangle(
                    triangles,
                    vertices[topRight],
                    vertices[bottomLeft],
                    vertices[bottomRight],
                    document);
            }
        }

        return triangles.Count > 0;
    }

    private static bool ReadPatches(
        int type,
        PdfDictionary dictionary,
        PdfDocumentCore document,
        MeshBitReader reader,
        int coordinateBits,
        int componentBits,
        double[] decode,
        PdfObject? function,
        PdfColorSpaceDefinition colorSpace,
        List<PdfMeshTriangle> triangles)
    {
        int flagBits = dictionary.GetValueOrNull("BitsPerFlag").AsInteger(document) ?? 0;
        if (flagBits is < 1 or > 8)
            return false;

        MeshPatch? previous = null;
        while (reader.TryRead(flagBits, out uint rawFlag))
        {
            int flag = (int)rawFlag;
            if (flag is < 0 or > 3 || previous is null && flag != 0)
                return false;
            int pointCount = type == 6
                ? flag == 0 ? 12 : 8
                : flag == 0 ? 16 : 12;
            int colorCount = flag == 0 ? 4 : 2;
            var points = new PdfPoint[pointCount];
            for (int index = 0; index < points.Length; index++)
            {
                if (!reader.TryRead(coordinateBits, out uint x) ||
                    !reader.TryRead(coordinateBits, out uint y))
                {
                    return triangles.Count > 0;
                }
                points[index] = new PdfPoint(
                    Decode(x, coordinateBits, decode[0], decode[1]),
                    Decode(y, coordinateBits, decode[2], decode[3]));
            }

            var colors = new PdfColor[colorCount];
            for (int index = 0; index < colors.Length; index++)
            {
                if (!TryReadColor(
                        reader,
                        componentBits,
                        decode,
                        function,
                        colorSpace,
                        document,
                        out colors[index]))
                {
                    return triangles.Count > 0;
                }
            }
            reader.Align();

            MeshPatch patch = CreatePatch(type, flag, points, colors, previous);
            Tessellate(patch, triangles, document);
            previous = patch;
        }

        return triangles.Count > 0;
    }

    private static MeshPatch CreatePatch(
        int type,
        int flag,
        IReadOnlyList<PdfPoint> points,
        IReadOnlyList<PdfColor> colors,
        MeshPatch? previous)
    {
        var patch = new MeshPatch();
        int offset = 0;
        if (flag == 0)
        {
            SetBoundary(patch, points, ref offset);
            patch.Colors[0] = colors[0];
            patch.Colors[1] = colors[1];
            patch.Colors[2] = colors[2];
            patch.Colors[3] = colors[3];
        }
        else
        {
            CopySharedEdge(patch, previous!, flag);
            SetRemainingBoundary(patch, points, ref offset);
            CopySharedColors(patch, previous!, flag);
            patch.Colors[2] = colors[0];
            patch.Colors[3] = colors[1];
        }

        if (type == 7)
        {
            patch.Points[1, 1] = points[offset++];
            patch.Points[1, 2] = points[offset++];
            patch.Points[2, 2] = points[offset++];
            patch.Points[2, 1] = points[offset];
        }
        else
        {
            CompleteCoonsInterior(patch);
        }
        return patch;
    }

    private static void SetBoundary(
        MeshPatch patch,
        IReadOnlyList<PdfPoint> points,
        ref int offset)
    {
        for (int column = 0; column < 4; column++)
            patch.Points[0, column] = points[offset++];
        for (int row = 1; row < 4; row++)
            patch.Points[row, 3] = points[offset++];
        for (int column = 2; column >= 0; column--)
            patch.Points[3, column] = points[offset++];
        for (int row = 2; row >= 1; row--)
            patch.Points[row, 0] = points[offset++];
    }

    private static void SetRemainingBoundary(
        MeshPatch patch,
        IReadOnlyList<PdfPoint> points,
        ref int offset)
    {
        for (int row = 1; row < 4; row++)
            patch.Points[row, 3] = points[offset++];
        for (int column = 2; column >= 0; column--)
            patch.Points[3, column] = points[offset++];
        for (int row = 2; row >= 1; row--)
            patch.Points[row, 0] = points[offset++];
    }

    private static void CopySharedEdge(MeshPatch patch, MeshPatch previous, int flag)
    {
        for (int index = 0; index < 4; index++)
        {
            patch.Points[0, index] = flag switch
            {
                1 => previous.Points[index, 3],
                2 => previous.Points[3, 3 - index],
                _ => previous.Points[3 - index, 0]
            };
        }
    }

    private static void CopySharedColors(MeshPatch patch, MeshPatch previous, int flag)
    {
        (patch.Colors[0], patch.Colors[1]) = flag switch
        {
            1 => (previous.Colors[1], previous.Colors[2]),
            2 => (previous.Colors[2], previous.Colors[3]),
            _ => (previous.Colors[3], previous.Colors[0])
        };
    }

    private static void CompleteCoonsInterior(MeshPatch patch)
    {
        patch.Points[1, 1] = CoonsInterior(patch, 0, 0);
        patch.Points[1, 2] = CoonsInterior(patch, 0, 3);
        patch.Points[2, 1] = CoonsInterior(patch, 3, 0);
        patch.Points[2, 2] = CoonsInterior(patch, 3, 3);
    }

    private static PdfPoint CoonsInterior(MeshPatch patch, int cornerRow, int cornerColumn)
    {
        int oppositeRow = 3 - cornerRow;
        int oppositeColumn = 3 - cornerColumn;
        int adjacentRow = cornerRow == 0 ? 1 : 2;
        int adjacentColumn = cornerColumn == 0 ? 1 : 2;
        PdfPoint corner = patch.Points[cornerRow, cornerColumn];
        PdfPoint rowAdjacent = patch.Points[cornerRow, adjacentColumn];
        PdfPoint columnAdjacent = patch.Points[adjacentRow, cornerColumn];
        PdfPoint farRow = patch.Points[cornerRow, oppositeColumn];
        PdfPoint farColumn = patch.Points[oppositeRow, cornerColumn];
        PdfPoint oppositeRowAdjacent = patch.Points[oppositeRow, adjacentColumn];
        PdfPoint oppositeColumnAdjacent = patch.Points[adjacentRow, oppositeColumn];
        PdfPoint opposite = patch.Points[oppositeRow, oppositeColumn];
        return new PdfPoint(
            (-4 * corner.X +
             6 * (rowAdjacent.X + columnAdjacent.X) -
             2 * (farRow.X + farColumn.X) +
             3 * (oppositeRowAdjacent.X + oppositeColumnAdjacent.X) -
             opposite.X) / 9,
            (-4 * corner.Y +
             6 * (rowAdjacent.Y + columnAdjacent.Y) -
             2 * (farRow.Y + farColumn.Y) +
             3 * (oppositeRowAdjacent.Y + oppositeColumnAdjacent.Y) -
             opposite.Y) / 9);
    }

    private static void Tessellate(
        MeshPatch patch,
        List<PdfMeshTriangle> triangles,
        PdfDocumentCore document)
    {
        int required = checked(PatchDivisions * PatchDivisions * 2);
        if (triangles.Count > document.Options.MaximumMeshTriangles - required)
            throw new PdfLimitException("Mesh shading exceeds the configured triangle limit.");

        var grid = new PdfMeshVertex[PatchDivisions + 1, PatchDivisions + 1];
        for (int row = 0; row <= PatchDivisions; row++)
        {
            double u = row / (double)PatchDivisions;
            for (int column = 0; column <= PatchDivisions; column++)
            {
                double v = column / (double)PatchDivisions;
                grid[row, column] = new PdfMeshVertex(
                    EvaluatePatchPoint(patch, u, v),
                    EvaluatePatchColor(patch, u, v));
            }
        }

        for (int row = 0; row < PatchDivisions; row++)
        {
            for (int column = 0; column < PatchDivisions; column++)
            {
                triangles.Add(new PdfMeshTriangle(
                    grid[row, column],
                    grid[row, column + 1],
                    grid[row + 1, column]));
                triangles.Add(new PdfMeshTriangle(
                    grid[row, column + 1],
                    grid[row + 1, column],
                    grid[row + 1, column + 1]));
            }
        }
    }

    private static PdfPoint EvaluatePatchPoint(MeshPatch patch, double u, double v)
    {
        double[] bu = Bernstein(u);
        double[] bv = Bernstein(v);
        double x = 0;
        double y = 0;
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                double weight = bu[row] * bv[column];
                x += patch.Points[row, column].X * weight;
                y += patch.Points[row, column].Y * weight;
            }
        }
        return new PdfPoint(x, y);
    }

    private static PdfColor EvaluatePatchColor(MeshPatch patch, double u, double v)
    {
        (double r00, double g00, double b00) = patch.Colors[0].ToRgb();
        (double r01, double g01, double b01) = patch.Colors[1].ToRgb();
        (double r11, double g11, double b11) = patch.Colors[2].ToRgb();
        (double r10, double g10, double b10) = patch.Colors[3].ToRgb();
        return PdfColor.Rgb(
            Bilinear(r00, r01, r11, r10, u, v),
            Bilinear(g00, g01, g11, g10, u, v),
            Bilinear(b00, b01, b11, b10, u, v));
    }

    private static double[] Bernstein(double value)
    {
        double inverse = 1 - value;
        return new[]
        {
            inverse * inverse * inverse,
            3 * value * inverse * inverse,
            3 * value * value * inverse,
            value * value * value
        };
    }

    private static double Bilinear(
        double topLeft,
        double topRight,
        double bottomRight,
        double bottomLeft,
        double u,
        double v) =>
        (1 - u) * ((1 - v) * topLeft + v * topRight) +
        u * ((1 - v) * bottomLeft + v * bottomRight);

    private static bool TryReadVertex(
        MeshBitReader reader,
        int coordinateBits,
        int componentBits,
        double[] decode,
        PdfObject? function,
        PdfColorSpaceDefinition colorSpace,
        PdfDocumentCore document,
        out PdfMeshVertex vertex)
    {
        vertex = default;
        if (!reader.TryRead(coordinateBits, out uint x) ||
            !reader.TryRead(coordinateBits, out uint y) ||
            !TryReadColor(
                reader,
                componentBits,
                decode,
                function,
                colorSpace,
                document,
                out PdfColor color))
        {
            return false;
        }
        vertex = new PdfMeshVertex(
            new PdfPoint(
                Decode(x, coordinateBits, decode[0], decode[1]),
                Decode(y, coordinateBits, decode[2], decode[3])),
            color);
        return true;
    }

    private static bool TryReadColor(
        MeshBitReader reader,
        int componentBits,
        double[] decode,
        PdfObject? function,
        PdfColorSpaceDefinition colorSpace,
        PdfDocumentCore document,
        out PdfColor color)
    {
        color = PdfColor.Black;
        int count = (decode.Length - 4) / 2;
        var components = new double[count];
        for (int index = 0; index < count; index++)
        {
            if (!reader.TryRead(componentBits, out uint component))
                return false;
            components[index] = Decode(
                component,
                componentBits,
                decode[4 + index * 2],
                decode[5 + index * 2]);
        }

        double[] converted = EvaluateFunctions(
            function,
            components,
            colorSpace.Components,
            document);
        color = colorSpace.Convert(converted);
        return true;
    }

    private static double[] EvaluateFunctions(
        PdfObject? value,
        double[] input,
        int outputCount,
        PdfDocumentCore document)
    {
        if (value is null)
            return input;
        PdfObject resolved = value.Resolve(document);
        if (resolved is PdfArray array)
        {
            var result = new double[outputCount];
            for (int index = 0; index < result.Length; index++)
            {
                PdfObject functionObject = array[Math.Min(index, array.Count - 1)];
                PdfFunction? function = PdfFunction.Create(
                    functionObject,
                    document,
                    expectedInputCount: input.Length,
                    expectedOutputCount: 1);
                result[index] = function?.Evaluate(input, 1)[0] ?? 0;
            }
            return result;
        }

        PdfFunction? parsed = PdfFunction.Create(
            resolved,
            document,
            expectedInputCount: input.Length,
            expectedOutputCount: outputCount);
        return parsed?.Evaluate(input, outputCount) ??
               new double[outputCount];
    }

    private static double Decode(uint value, int bits, double minimum, double maximum)
    {
        double denominator = bits == 32 ? uint.MaxValue : (1UL << bits) - 1;
        return minimum + value / denominator * (maximum - minimum);
    }

    private static void AddTriangle(
        List<PdfMeshTriangle> triangles,
        PdfMeshVertex first,
        PdfMeshVertex second,
        PdfMeshVertex third,
        PdfDocumentCore document)
    {
        if (triangles.Count >= document.Options.MaximumMeshTriangles)
            throw new PdfLimitException("Mesh shading exceeds the configured triangle limit.");
        triangles.Add(new PdfMeshTriangle(first, second, third));
    }

    private sealed class MeshPatch
    {
        public PdfPoint[,] Points { get; } = new PdfPoint[4, 4];
        public PdfColor[] Colors { get; } = new PdfColor[4];
    }

    private sealed class MeshBitReader
    {
        private readonly byte[] _bytes;
        private int _bitOffset;

        public MeshBitReader(byte[] bytes) => _bytes = bytes;

        public bool TryRead(int count, out uint value)
        {
            value = 0;
            if (count is < 1 or > 32 ||
                _bitOffset > _bytes.Length * 8 - count)
            {
                return false;
            }

            for (int index = 0; index < count; index++)
            {
                int bit = (_bytes[_bitOffset >> 3] >>
                           (7 - (_bitOffset & 7))) & 1;
                value = (value << 1) | (uint)bit;
                _bitOffset++;
            }
            return true;
        }

        public void Align() => _bitOffset = (_bitOffset + 7) & ~7;
    }
}
