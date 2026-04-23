using System;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteGeometryAssetAssemblerTests
{
    [Fact]
    public async Task PrepareHeightMapGridAsync_PreparesBorderSkirtFallbackThatExtendsBelowBaseHeight()
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
                includeBorderSkirtFallback: true,
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
        Assert.Equal(3.0f, importedSkirtMesh.Positions[0].z);
        Assert.Equal(2.0f, importedSkirtMesh.Positions[1].z);
        float minimumZ = float.MaxValue;
        foreach (float3 position in importedSkirtMesh.Positions)
        {
            minimumZ = Math.Min(minimumZ, position.z);
        }

        Assert.True(minimumZ < 0.0f);
        TriangleSubmeshRawData submesh = Assert.IsType<TriangleSubmeshRawData>(Assert.Single(importedSkirtMesh.Submeshes));
        Assert.True(ComputeTriangleNormalY(importedSkirtMesh, submesh.Indices[0], submesh.Indices[1], submesh.Indices[2]) < 0.0f);
        Assert.True(ComputeTriangleNormalY(importedSkirtMesh, submesh.Indices[6], submesh.Indices[7], submesh.Indices[8]) > 0.0f);
    }

    [Fact]
    public async Task PrepareHeightMapGridAsync_SkipsBorderSkirtFallbackWhenNotEligible()
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
                includeBorderSkirtFallback: false,
                new ResoniteRawHdrTextureImport(2, 2, new byte[2 * 2 * 4 * sizeof(float)]),
                uvScale: null,
                uvOffset: null,
                progressReporter: null,
                cancellationToken: CancellationToken.None));

        Assert.Empty(batch.VisualFallbackAssets ?? []);
        Assert.Empty(client.ImportedMeshes);
    }

    private static float ComputeTriangleNormalY(ImportMeshRawData mesh, int firstIndex, int secondIndex, int thirdIndex)
    {
        float3 first = mesh.Positions[firstIndex];
        float3 second = mesh.Positions[secondIndex];
        float3 third = mesh.Positions[thirdIndex];
        float ax = second.x - first.x;
        float ay = second.y - first.y;
        float az = second.z - first.z;
        float bx = third.x - first.x;
        float by = third.y - first.y;
        float bz = third.z - first.z;
        return (az * bx) - (ax * bz);
    }
}
