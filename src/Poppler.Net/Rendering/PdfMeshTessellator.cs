using Poppler.Graphics;

namespace Poppler.Rendering;

/// <summary>
/// Deterministic adaptive tessellation for PDF type 6 and 7 patch meshes.
/// Geometry error is measured after the complete source-to-device transform;
/// color error is measured independently in managed RGB space.
/// </summary>
internal static class PdfMeshTessellator
{
    private const int MaximumSubdivisionLevel = 10;
    private const double GeometryTolerance = 0.25;
    private const double ColorTolerance = 1d / 255;

    internal static IReadOnlyList<PdfMeshTriangle> Tessellate(
        PdfMeshShadingBrush mesh,
        PdfMatrix sourceToDevice,
        int maximumTriangles)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        IReadOnlyList<PdfMeshPatch> patches = mesh.Patches;
        if (patches.Count == 0)
            return mesh.Triangles;

        long minimumTriangles = checked((long)patches.Count * 2);
        if (maximumTriangles < minimumTriangles)
            throw TriangleLimit();

        int maximumLevel = MaximumLevelFor(maximumTriangles);
        var requiredLevels = new int[patches.Count];
        for (int index = 0; index < patches.Count; index++)
        {
            requiredLevels[index] = RequiredLevel(
                patches[index],
                sourceToDevice,
                u0: 0,
                v0: 0,
                u1: 1,
                v1: 1,
                depth: 0,
                maximumLevel);
        }

        int[] parents = Enumerable.Range(0, patches.Count).ToArray();
        var edgeOwners = new Dictionary<PdfMeshPatchEdge, int>(
            ReferenceEqualityComparer.Instance);
        for (int patchIndex = 0; patchIndex < patches.Count; patchIndex++)
        {
            foreach (PdfMeshPatchEdgeSide side in Enum.GetValues<PdfMeshPatchEdgeSide>())
            {
                PdfMeshPatchEdge edge = patches[patchIndex].GetEdge(side);
                if (edgeOwners.TryGetValue(edge, out int owner))
                    Union(parents, patchIndex, owner);
                else
                    edgeOwners.Add(edge, patchIndex);
            }
        }

        var componentLevels = new int[patches.Count];
        for (int index = 0; index < patches.Count; index++)
        {
            int root = Find(parents, index);
            componentLevels[root] = Math.Max(
                componentLevels[root],
                requiredLevels[index]);
        }

        long totalTriangles = 0;
        for (int index = 0; index < patches.Count; index++)
        {
            int level = componentLevels[Find(parents, index)];
            totalTriangles = checked(totalTriangles + TriangleCount(level));
            if (totalTriangles > maximumTriangles)
                throw TriangleLimit();
            requiredLevels[index] = level;
        }

        var result = new PdfMeshTriangle[(int)totalTriangles];
        int triangleOffset = 0;
        for (int index = 0; index < patches.Count; index++)
        {
            TessellatePatch(
                patches[index],
                requiredLevels[index],
                result,
                ref triangleOffset);
        }
        return result;
    }

    private static int RequiredLevel(
        PdfMeshPatch patch,
        PdfMatrix sourceToDevice,
        double u0,
        double v0,
        double u1,
        double v1,
        int depth,
        int maximumLevel)
    {
        if (WithinTolerance(patch, sourceToDevice, u0, v0, u1, v1))
            return depth;
        if (depth >= maximumLevel || depth >= MaximumSubdivisionLevel)
            throw TriangleLimit();

        double middleU = (u0 + u1) * 0.5;
        double middleV = (v0 + v1) * 0.5;
        int next = depth + 1;
        return Math.Max(
            Math.Max(
                RequiredLevel(
                    patch,
                    sourceToDevice,
                    u0,
                    v0,
                    middleU,
                    middleV,
                    next,
                    maximumLevel),
                RequiredLevel(
                    patch,
                    sourceToDevice,
                    u0,
                    middleV,
                    middleU,
                    v1,
                    next,
                    maximumLevel)),
            Math.Max(
                RequiredLevel(
                    patch,
                    sourceToDevice,
                    middleU,
                    v0,
                    u1,
                    middleV,
                    next,
                    maximumLevel),
                RequiredLevel(
                    patch,
                    sourceToDevice,
                    middleU,
                    middleV,
                    u1,
                    v1,
                    next,
                    maximumLevel)));
    }

    private static bool WithinTolerance(
        PdfMeshPatch patch,
        PdfMatrix transform,
        double u0,
        double v0,
        double u1,
        double v1)
    {
        PdfPoint first = DevicePoint(patch, transform, u0, v0);
        PdfPoint second = DevicePoint(patch, transform, u0, v1);
        PdfPoint third = DevicePoint(patch, transform, u1, v0);
        PdfPoint fourth = DevicePoint(patch, transform, u1, v1);
        double middleU = (u0 + u1) * 0.5;
        double middleV = (v0 + v1) * 0.5;
        double firstCentroidU = (u0 + u0 + u1) / 3;
        double firstCentroidV = (v0 + v1 + v0) / 3;
        double secondCentroidU = (u0 + u1 + u1) / 3;
        double secondCentroidV = (v1 + v0 + v1) / 3;

        if (!Near(
                DevicePoint(patch, transform, u0, middleV),
                Average(first, second)) ||
            !Near(
                DevicePoint(patch, transform, u1, middleV),
                Average(third, fourth)) ||
            !Near(
                DevicePoint(patch, transform, middleU, v0),
                Average(first, third)) ||
            !Near(
                DevicePoint(patch, transform, middleU, v1),
                Average(second, fourth)) ||
            !Near(
                DevicePoint(patch, transform, middleU, middleV),
                Average(second, third)) ||
            !Near(
                DevicePoint(patch, transform, firstCentroidU, firstCentroidV),
                Average(first, second, third)) ||
            !Near(
                DevicePoint(patch, transform, secondCentroidU, secondCentroidV),
                Average(second, third, fourth)))
        {
            return false;
        }

        PdfColor firstColor = patch.EvaluateColor(u0, v0);
        PdfColor secondColor = patch.EvaluateColor(u0, v1);
        PdfColor thirdColor = patch.EvaluateColor(u1, v0);
        PdfColor fourthColor = patch.EvaluateColor(u1, v1);
        return ColorNear(
                   patch.EvaluateColor(middleU, middleV),
                   secondColor,
                   thirdColor) &&
               ColorNear(
                   patch.EvaluateColor(firstCentroidU, firstCentroidV),
                   firstColor,
                   secondColor,
                   thirdColor) &&
               ColorNear(
                   patch.EvaluateColor(secondCentroidU, secondCentroidV),
                   secondColor,
                   thirdColor,
                   fourthColor);
    }

    private static void TessellatePatch(
        PdfMeshPatch patch,
        int level,
        PdfMeshTriangle[] triangles,
        ref int triangleOffset)
    {
        int divisions = 1 << level;
        var grid = new PdfMeshVertex[divisions + 1, divisions + 1];
        for (int row = 0; row <= divisions; row++)
        {
            double u = row / (double)divisions;
            for (int column = 0; column <= divisions; column++)
            {
                double v = column / (double)divisions;
                grid[row, column] = BoundaryVertex(
                    patch,
                    row,
                    column,
                    divisions,
                    u,
                    v);
            }
        }

        for (int row = 0; row < divisions; row++)
        {
            for (int column = 0; column < divisions; column++)
            {
                triangles[triangleOffset++] = new PdfMeshTriangle(
                    grid[row, column],
                    grid[row, column + 1],
                    grid[row + 1, column]);
                triangles[triangleOffset++] = new PdfMeshTriangle(
                    grid[row, column + 1],
                    grid[row + 1, column],
                    grid[row + 1, column + 1]);
            }
        }
    }

    private static PdfMeshVertex BoundaryVertex(
        PdfMeshPatch patch,
        int row,
        int column,
        int divisions,
        double u,
        double v)
    {
        PdfMeshPatchEdge? edge = null;
        double parameter = 0;
        if (row == 0)
        {
            edge = patch.GetEdge(PdfMeshPatchEdgeSide.Top);
            parameter = v;
        }
        else if (column == divisions)
        {
            edge = patch.GetEdge(PdfMeshPatchEdgeSide.Right);
            parameter = u;
        }
        else if (row == divisions)
        {
            edge = patch.GetEdge(PdfMeshPatchEdgeSide.Bottom);
            parameter = 1 - v;
        }
        else if (column == 0)
        {
            edge = patch.GetEdge(PdfMeshPatchEdgeSide.Left);
            parameter = 1 - u;
        }

        return edge is null
            ? new PdfMeshVertex(
                patch.EvaluatePoint(u, v),
                patch.EvaluateColor(u, v))
            : new PdfMeshVertex(
                edge.EvaluatePoint(parameter),
                edge.EvaluateColor(parameter));
    }

    private static PdfPoint DevicePoint(
        PdfMeshPatch patch,
        PdfMatrix transform,
        double u,
        double v)
    {
        PdfPoint point = patch.EvaluatePoint(u, v);
        return transform.Transform(point.X, point.Y);
    }

    private static bool Near(PdfPoint actual, PdfPoint expected)
    {
        double x = actual.X - expected.X;
        double y = actual.Y - expected.Y;
        double error = x * x + y * y;
        return double.IsFinite(error) &&
               error <= GeometryTolerance * GeometryTolerance;
    }

    private static bool ColorNear(PdfColor actual, PdfColor first, PdfColor second)
    {
        (double firstRed, double firstGreen, double firstBlue) = first.ToRgb();
        (double secondRed, double secondGreen, double secondBlue) = second.ToRgb();
        return ColorNear(
            actual,
            (firstRed + secondRed) * 0.5,
            (firstGreen + secondGreen) * 0.5,
            (firstBlue + secondBlue) * 0.5);
    }

    private static bool ColorNear(
        PdfColor actual,
        PdfColor first,
        PdfColor second,
        PdfColor third)
    {
        (double firstRed, double firstGreen, double firstBlue) = first.ToRgb();
        (double secondRed, double secondGreen, double secondBlue) = second.ToRgb();
        (double thirdRed, double thirdGreen, double thirdBlue) = third.ToRgb();
        return ColorNear(
            actual,
            (firstRed + secondRed + thirdRed) / 3,
            (firstGreen + secondGreen + thirdGreen) / 3,
            (firstBlue + secondBlue + thirdBlue) / 3);
    }

    private static bool ColorNear(
        PdfColor actual,
        double expectedRed,
        double expectedGreen,
        double expectedBlue)
    {
        (double red, double green, double blue) = actual.ToRgb();
        double error = Math.Max(
            Math.Abs(red - expectedRed),
            Math.Max(
                Math.Abs(green - expectedGreen),
                Math.Abs(blue - expectedBlue)));
        return double.IsFinite(error) && error <= ColorTolerance;
    }

    private static PdfPoint Average(PdfPoint first, PdfPoint second) =>
        new((first.X + second.X) * 0.5, (first.Y + second.Y) * 0.5);

    private static PdfPoint Average(
        PdfPoint first,
        PdfPoint second,
        PdfPoint third) =>
        new(
            (first.X + second.X + third.X) / 3,
            (first.Y + second.Y + third.Y) / 3);

    private static int MaximumLevelFor(int maximumTriangles)
    {
        int level = 0;
        while (level < MaximumSubdivisionLevel &&
               TriangleCount(level + 1) <= maximumTriangles)
        {
            level++;
        }
        return level;
    }

    private static long TriangleCount(int level)
    {
        long divisions = 1L << level;
        return checked(2 * divisions * divisions);
    }

    private static int Find(int[] parents, int index)
    {
        while (parents[index] != index)
        {
            parents[index] = parents[parents[index]];
            index = parents[index];
        }
        return index;
    }

    private static void Union(int[] parents, int first, int second)
    {
        int firstRoot = Find(parents, first);
        int secondRoot = Find(parents, second);
        if (firstRoot == secondRoot)
            return;
        if (firstRoot < secondRoot)
            parents[secondRoot] = firstRoot;
        else
            parents[firstRoot] = secondRoot;
    }

    private static PdfLimitException TriangleLimit() =>
        new("Adaptive mesh tessellation exceeds the configured triangle limit.");
}
