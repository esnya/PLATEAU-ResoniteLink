using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Execution;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteGeometryAssetAssemblerTests
{
    [Fact]
    public async Task PrepareHeightMapGridAsync_PreparesBorderSkirtFallbackBehindGeometrySeam()
    {
        using SceneBuilderRecordingClient client = new();
        ResoniteGeometryAssetAssembler assembler = new();

        PreparedHeightMapGridAssetBatch batch = Assert.IsType<PreparedHeightMapGridAssetBatch>(
            await assembler.PrepareHeightMapGridAsync(
                client,
                "HeightMap Terrain",
                "HeightMap Terrain_heightmap",
                "HeightMap Terrain",
                new ResoniteHeightMapGridGeometry(
                    Width: 2,
                    Height: 2,
                    Size: new ResoniteFloat2(10.0, 10.0),
                    MinHeight: 0.0,
                    MaxHeight: 3.0,
                    HeightSamples: [0.0, 1.0, 2.0, 3.0]),
                new ResoniteRawHdrTextureImport(2, 2, new byte[2 * 2 * 4 * sizeof(float)]),
                uvScale: null,
                uvOffset: null,
                includeBorderSkirtFallback: true,
                progressReporter: null,
                cancellationToken: CancellationToken.None));

        Assert.NotNull(batch.VisualFallbackAssets);
        PreparedTriangleMeshAssetBatch skirtAsset = Assert.Single(batch.VisualFallbackAssets);
        ImportMeshRawData importedSkirtMesh = Assert.Single(client.ImportedMeshes);
        TriangleSubmeshRawData skirtSubmesh = Assert.IsType<TriangleSubmeshRawData>(Assert.Single(importedSkirtMesh.Submeshes));

        Assert.Equal("HeightMap Terrain_heightmap_skirt", skirtAsset.MeshAssetSlotName);
        Assert.Equal("resdb:///mesh/0", skirtAsset.MeshUri.ToString());
        Assert.Equal(16, importedSkirtMesh.VertexCount);
        Assert.Equal(8, skirtSubmesh.TriangleCount);
    }
}
