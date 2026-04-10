using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ResoniteMeshImportFactoryTests
{
    [Fact]
    public void CreateOrdersSubmeshesAndExportsVertexColors()
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
                    new ResoniteFloat3(4.0, 5.0, 6.0),
                    new ResoniteFloat3(1.0, 0.0, 0.0),
                    new ResoniteFloat2(0.5, 0.25)),
                new ResoniteMeshVertex(
                    new ResoniteFloat3(7.0, 8.0, 9.0),
                    new ResoniteFloat3(0.0, 0.0, 1.0),
                    new ResoniteFloat2(1.0, 0.0),
                    new ResoniteColor(0.1, 0.2, 0.3, 0.4)),
            ],
            Submeshes:
            [
                new ResoniteMeshSubmesh(1, "later", [2, 1, 0]),
                new ResoniteMeshSubmesh(0, "first", [0, 1, 2]),
            ]);

        ImportMeshRawData result = ResoniteMeshImportFactory.Create(mesh);

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

        Assert.Equal(0.2f, result.Colors[0].r, 6);
        Assert.Equal(0.4f, result.Colors[0].g, 6);
        Assert.Equal(0.6f, result.Colors[0].b, 6);
        Assert.Equal(0.8f, result.Colors[0].a, 6);

        Assert.Equal(1.0f, result.Colors[1].r, 6);
        Assert.Equal(1.0f, result.Colors[1].g, 6);
        Assert.Equal(1.0f, result.Colors[1].b, 6);
        Assert.Equal(1.0f, result.Colors[1].a, 6);
    }
}
