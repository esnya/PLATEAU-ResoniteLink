using System;
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
    public async Task PrepareHeightMapGridAsync_PreparesBorderSkirtFallbackWithDepthBasedOnHeightRange()
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
                progressReporter: null,
                cancellationToken: CancellationToken.None));

        PreparedTriangleMeshAssetBatch skirtAsset = Assert.Single(batch.VisualFallbackAssets ?? []);
        ImportMeshRawData importedSkirtMesh = Assert.Single(client.ImportedMeshes);

        Assert.Equal("resdb:///mesh/0", skirtAsset.MeshUri.ToString());
        Assert.True(importedSkirtMesh.VertexCount > 0);
        Assert.NotEmpty(importedSkirtMesh.Submeshes);
        float minimumZ = float.MaxValue;
        foreach (float3 position in importedSkirtMesh.Positions)
        {
            minimumZ = Math.Min(minimumZ, position.z);
        }

        Assert.True(minimumZ <= -6.0f);
    }
}
