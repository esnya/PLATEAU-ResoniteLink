using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteMeshImportFactoryTests
{
    [Fact]
    public async Task CreateOrdersSubmeshesAndExportsVertexColors()
    {
        ResoniteImportedMesh mesh = new(
            Vertices:
            [
                new ResoniteMeshVertex(
                    new ResoniteFloat3(1.0, 2.0, 3.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(0.25, 0.75),
                    new ResoniteColor(0.2, 0.4, 0.6, 0.8)),
                new ResoniteMeshVertex(
                    new ResoniteFloat3(4.0, 6.0, 5.0),
                    new ResoniteFloat3(1.0, 0.0, 0.0),
                    new ResoniteFloat2(0.5, 0.25)),
                new ResoniteMeshVertex(
                    new ResoniteFloat3(7.0, 8.0, 10.0),
                    new ResoniteFloat3(0.0, 0.0, 1.0),
                    new ResoniteFloat2(1.0, 0.0),
                    new ResoniteColor(0.1, 0.2, 0.3, 0.4)),
            ],
            Submeshes:
            [
                new ResoniteMeshSubmesh(3, [2, 1, 0]),
                new ResoniteMeshSubmesh(1, [0, 1, 2]),
            ]);

        ImportMeshRawData result = await GeometryImportSourceMaterializer.MaterializeRawAsync(
            ResoniteMeshImportFactory.Create(mesh),
            CancellationToken.None);

        Assert.True(result.HasColors);
        Assert.Equal(2, result.Submeshes.Count);

        TriangleSubmeshRawData firstSubmesh = Assert.IsType<TriangleSubmeshRawData>(result.Submeshes[0]);
        TriangleSubmeshRawData secondSubmesh = Assert.IsType<TriangleSubmeshRawData>(result.Submeshes[1]);
        Assert.Equal([0, 1, 2], firstSubmesh.Indices);
        Assert.Equal([2, 1, 0], secondSubmesh.Indices);

        Assert.Equal(1.0f, result.Positions[0].x);
        Assert.Equal(2.0f, result.Positions[0].y);
        Assert.Equal(3.0f, result.Positions[0].z);
        Assert.Equal(0.25f, result.AccessUV_2D(0)[0].x);
        Assert.Equal(0.75f, result.AccessUV_2D(0)[0].y);

        Assert.Equal(ToLinear(0.2), result.Colors[0].r, 6);
        Assert.Equal(ToLinear(0.4), result.Colors[0].g, 6);
        Assert.Equal(ToLinear(0.6), result.Colors[0].b, 6);
        Assert.Equal(0.8f, result.Colors[0].a, 6);

        Assert.Equal(1.0f, result.Colors[1].r, 6);
        Assert.Equal(1.0f, result.Colors[1].g, 6);
        Assert.Equal(1.0f, result.Colors[1].b, 6);
        Assert.Equal(1.0f, result.Colors[1].a, 6);
    }

    [Fact]
    public async Task CreateAcceptsTriangleIndicesBeyondUInt16Range()
    {
        ResoniteMeshVertex[] vertices = Enumerable.Range(0, 65_537)
            .Select(static index => new ResoniteMeshVertex(
                new ResoniteFloat3(index, 0.0, 0.0),
                new ResoniteFloat3(0.0, 1.0, 0.0),
                new ResoniteFloat2(0.0, 0.0)))
            .ToArray();
        vertices[0] = new ResoniteMeshVertex(
            new ResoniteFloat3(0.0, 0.0, 0.0),
            new ResoniteFloat3(0.0, 1.0, 0.0),
            new ResoniteFloat2(0.0, 0.0));
        vertices[65_535] = new ResoniteMeshVertex(
            new ResoniteFloat3(1.0, 0.0, 0.0),
            new ResoniteFloat3(0.0, 1.0, 0.0),
            new ResoniteFloat2(1.0, 0.0));
        vertices[65_536] = new ResoniteMeshVertex(
            new ResoniteFloat3(0.0, 0.0, 1.0),
            new ResoniteFloat3(0.0, 1.0, 0.0),
            new ResoniteFloat2(0.0, 1.0));

        ResoniteImportedMesh mesh = new(
            Vertices: vertices,
            Submeshes:
            [
                new ResoniteMeshSubmesh(0, [0, 65_535, 65_536]),
            ]);

        ImportMeshRawData result = await GeometryImportSourceMaterializer.MaterializeRawAsync(
            ResoniteMeshImportFactory.Create(mesh),
            CancellationToken.None);

        TriangleSubmeshRawData submesh = Assert.IsType<TriangleSubmeshRawData>(Assert.Single(result.Submeshes));
        Assert.Equal(65_537, result.VertexCount);
        Assert.Equal([0, 65_535, 65_536], submesh.Indices);
    }

    [Fact]
    public void CreateLinearVertexColorMatchesSharedColorSpaceHelper()
    {
        ResoniteColor color = new(0.2, 0.4, 0.6, 0.8);

        color linear = ResoniteColorSpace.CreateLinearVertexColor(color);

        Assert.Equal(ToLinear(0.2), linear.r, 6);
        Assert.Equal(ToLinear(0.4), linear.g, 6);
        Assert.Equal(ToLinear(0.6), linear.b, 6);
        Assert.Equal(0.8f, linear.a, 6);
    }

    [Fact]
    public void CreateRejectsOutOfRangeTriangleIndices()
    {
        ResoniteImportedMesh mesh = new(
            Vertices:
            [
                new ResoniteMeshVertex(
                    new ResoniteFloat3(0.0, 0.0, 0.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(
                    new ResoniteFloat3(1.0, 0.0, 0.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(
                    new ResoniteFloat3(0.0, 0.0, 1.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(0.0, 1.0)),
            ],
            Submeshes:
            [
                new ResoniteMeshSubmesh(0, [0, 1, 99]),
            ]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => ResoniteMeshImportFactory.Create(mesh));

        Assert.Contains("referenced vertex index 99", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRejectsNonFiniteVertexData()
    {
        ResoniteImportedMesh mesh = new(
            Vertices:
            [
                new ResoniteMeshVertex(
                    new ResoniteFloat3(double.NaN, 0.0, 0.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(
                    new ResoniteFloat3(1.0, 0.0, 0.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(
                    new ResoniteFloat3(0.0, 0.0, 1.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(0.0, 1.0)),
            ],
            Submeshes:
            [
                new ResoniteMeshSubmesh(0, [0, 1, 2]),
            ]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => ResoniteMeshImportFactory.Create(mesh));

        Assert.Contains("non-finite position.x", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRejectsVertexDataThatOverflowsFloat()
    {
        ResoniteImportedMesh mesh = new(
            Vertices:
            [
                new ResoniteMeshVertex(
                    new ResoniteFloat3((double)float.MaxValue * 2.0, 0.0, 0.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(
                    new ResoniteFloat3(1.0, 0.0, 0.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(
                    new ResoniteFloat3(0.0, 0.0, 1.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(0.0, 1.0)),
            ],
            Submeshes:
            [
                new ResoniteMeshSubmesh(0, [0, 1, 2]),
            ]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => ResoniteMeshImportFactory.Create(mesh));

        Assert.Contains("not representable as float", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRejectsZeroLengthNormals()
    {
        ResoniteImportedMesh mesh = new(
            Vertices:
            [
                new ResoniteMeshVertex(
                    new ResoniteFloat3(0.0, 0.0, 0.0),
                    new ResoniteFloat3(0.0, 0.0, 0.0),
                    new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(
                    new ResoniteFloat3(1.0, 0.0, 0.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(
                    new ResoniteFloat3(0.0, 0.0, 1.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(0.0, 1.0)),
            ],
            Submeshes:
            [
                new ResoniteMeshSubmesh(0, [0, 1, 2]),
            ]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => ResoniteMeshImportFactory.Create(mesh));

        Assert.Contains("zero-length normal", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRejectsDegenerateTriangles()
    {
        ResoniteImportedMesh mesh = new(
            Vertices:
            [
                new ResoniteMeshVertex(
                    new ResoniteFloat3(0.0, 0.0, 0.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(
                    new ResoniteFloat3(1.0, 0.0, 0.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(
                    new ResoniteFloat3(2.0, 0.0, 0.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(0.0, 1.0)),
            ],
            Submeshes:
            [
                new ResoniteMeshSubmesh(0, [0, 1, 2]),
            ]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => ResoniteMeshImportFactory.Create(mesh));

        Assert.Contains("degenerate triangle", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRejectsTriangleMeshWithoutAnySubmesh()
    {
        ResoniteImportedMesh mesh = new(
            Vertices:
            [
                new ResoniteMeshVertex(
                    new ResoniteFloat3(0.0, 0.0, 0.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(
                    new ResoniteFloat3(1.0, 0.0, 0.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(
                    new ResoniteFloat3(0.0, 0.0, 1.0),
                    new ResoniteFloat3(0.0, 1.0, 0.0),
                    new ResoniteFloat2(0.0, 1.0)),
            ],
            Submeshes: []);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => ResoniteMeshImportFactory.Create(mesh));

        Assert.Contains("did not contain any submesh", exception.Message, StringComparison.Ordinal);
    }

    private static float ToLinear(double value)
    {
        double linear = value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
        return (float)linear;
    }
}
