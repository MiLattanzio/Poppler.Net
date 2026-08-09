using Poppler;
using Poppler.Graphics;
using Poppler.Rendering;

namespace Poppler.Net.Tests;

public sealed class AdaptiveMeshAlpha3Tests
{
    [Test]
    public void GeometricSubdivisionTracksDeviceScale()
    {
        PdfMeshShadingBrush mesh = Mesh(CurvedPatch());

        int small = PdfMeshTessellator.Tessellate(
            mesh,
            Scale(0.1),
            maximumTriangles: 65_536).Count;
        int large = PdfMeshTessellator.Tessellate(
            mesh,
            Scale(64),
            maximumTriangles: 65_536).Count;

        Assert.Multiple((Action)(() =>
        {
            Assert.That(small, Is.EqualTo(2));
            Assert.That(large, Is.GreaterThan(small));
            Assert.That(large, Is.LessThanOrEqualTo(65_536));
        }));
    }

    [Test]
    public void ColorErrorIsIndependentFromDeviceScale()
    {
        PdfPoint[,] points = RegularGrid();
        var patch = new PdfMeshPatch(
            points,
            new[]
            {
                PdfColor.Rgb(1, 0, 0),
                PdfColor.Rgb(0, 1, 0),
                PdfColor.Rgb(0, 0, 1),
                PdfColor.Rgb(1, 1, 1)
            });

        int triangles = PdfMeshTessellator.Tessellate(
            Mesh(patch),
            Scale(0.01),
            maximumTriangles: 65_536).Count;

        Assert.That(triangles, Is.GreaterThan(2));
    }

    [Test]
    public void ConnectedPatchesUseIdenticalSharedEdgeSubdivision()
    {
        PdfMeshPatch first = CurvedPatch();
        PdfMeshPatchEdge shared = first.GetEdge(PdfMeshPatchEdgeSide.Right);
        PdfPoint[,] secondPoints = AdjacentGrid(shared);
        PdfColor black = PdfColor.Black;
        var second = new PdfMeshPatch(
            secondPoints,
            new[] { black, black, black, black },
            shared);
        PdfMatrix transform = Scale(64);

        int firstCount = PdfMeshTessellator.Tessellate(
            Mesh(first),
            transform,
            maximumTriangles: 65_536).Count;
        int secondCount = PdfMeshTessellator.Tessellate(
            Mesh(second),
            transform,
            maximumTriangles: 65_536).Count;
        IReadOnlyList<PdfMeshTriangle> connected = PdfMeshTessellator.Tessellate(
            Mesh(first, second),
            transform,
            maximumTriangles: 65_536);
        int perPatch = connected.Count / 2;
        int divisions = (int)Math.Sqrt(perPatch / 2d);
        PdfPoint[] expectedEdge = Enumerable.Range(0, divisions + 1)
            .Select(index => shared.EvaluatePoint(index / (double)divisions))
            .ToArray();
        HashSet<PdfPoint> firstVertices = Vertices(connected, 0, perPatch);
        HashSet<PdfPoint> secondVertices = Vertices(
            connected,
            perPatch,
            perPatch);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                connected.Count,
                Is.EqualTo(2 * Math.Max(firstCount, secondCount)));
            Assert.That(expectedEdge.All(firstVertices.Contains), Is.True);
            Assert.That(expectedEdge.All(secondVertices.Contains), Is.True);
            Assert.That(expectedEdge, Has.Length.GreaterThan(2));
        }));
    }

    [Test]
    public void TessellationIsDeterministicAndFailsBeforeExceedingTheLimit()
    {
        PdfMeshShadingBrush mesh = Mesh(CurvedPatch());
        PdfMatrix transform = Scale(64);
        IReadOnlyList<PdfMeshTriangle> first = PdfMeshTessellator.Tessellate(
            mesh,
            transform,
            maximumTriangles: 65_536);
        IReadOnlyList<PdfMeshTriangle> second = PdfMeshTessellator.Tessellate(
            mesh,
            transform,
            maximumTriangles: 65_536);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(
                (Action)(() => PdfMeshTessellator.Tessellate(
                    mesh,
                    transform,
                    maximumTriangles: first.Count - 1)),
                Throws.TypeOf<PdfLimitException>());
        }));
    }

    private static PdfMeshPatch CurvedPatch()
    {
        PdfPoint[,] points = RegularGrid();
        points[0, 1] = new PdfPoint(1d / 3, -1);
        points[0, 2] = new PdfPoint(2d / 3, -1);
        points[1, 3] = new PdfPoint(1.5, 1d / 3);
        points[2, 3] = new PdfPoint(1.5, 2d / 3);
        points[3, 1] = new PdfPoint(1d / 3, 2);
        points[3, 2] = new PdfPoint(2d / 3, 2);
        PdfColor black = PdfColor.Black;
        return new PdfMeshPatch(
            points,
            new[] { black, black, black, black });
    }

    private static PdfPoint[,] RegularGrid()
    {
        var points = new PdfPoint[4, 4];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                points[row, column] = new PdfPoint(
                    column / 3d,
                    row / 3d);
            }
        }
        return points;
    }

    private static PdfPoint[,] AdjacentGrid(PdfMeshPatchEdge shared)
    {
        var points = new PdfPoint[4, 4];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                points[row, column] = new PdfPoint(
                    1 + row / 3d,
                    column / 3d);
            }
        }
        for (int index = 0; index < 4; index++)
            points[0, index] = shared.GetControlPoint(index);
        return points;
    }

    private static PdfMeshShadingBrush Mesh(params PdfMeshPatch[] patches) =>
        new(
            PdfShadingKind.CoonsPatch,
            Array.Empty<PdfMeshTriangle>(),
            PdfMatrix.Identity,
            patches);

    private static PdfMatrix Scale(double value) =>
        new(value, 0, 0, value, 0, 0);

    private static HashSet<PdfPoint> Vertices(
        IReadOnlyList<PdfMeshTriangle> triangles,
        int offset,
        int count) =>
        triangles
            .Skip(offset)
            .Take(count)
            .SelectMany(triangle => new[]
            {
                triangle.First.Point,
                triangle.Second.Point,
                triangle.Third.Point
            })
            .ToHashSet();
}
