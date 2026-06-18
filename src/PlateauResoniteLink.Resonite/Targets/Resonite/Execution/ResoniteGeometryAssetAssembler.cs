using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


using PlateauResoniteLink.Core.Diagnostics;

using PlateauResoniteLink.Resonite.Transport.ResoniteLink;

using ResoniteLink;
using PlateauResoniteLink.Core.Application.Importing.Contracts;

namespace PlateauResoniteLink.Resonite.Targets.Resonite.Execution;

internal static class ResoniteGeometryAssetAssembler
{
    public static async Task<UploadedTriangleMeshAssetBatch> PrepareTriangleMeshAsync(
        IResoniteLinkClient importClient,
        string displayName,
        IGeometryImportSource meshSource,
        CancellationToken cancellationToken)
    {
        PlateauDiagnostics.Verbose(
            "Mesh '{DisplayName}' importing triangle mesh (vertices={VertexCount}, submeshes={SubmeshCount}).",
            displayName,
            meshSource.VertexCount,
            meshSource.SubmeshCount);
        Uri assetUri = await importClient.ImportMeshAsync(meshSource, cancellationToken);
        PlateauDiagnostics.Verbose(
            "Mesh '{DisplayName}' mesh import completed -> '{AssetUri}'.",
            displayName,
            assetUri);
        return new UploadedTriangleMeshAssetBatch(assetUri);
    }

    public static async Task<UploadedTerrainGridAssetBatch> PrepareTerrainGridAsync(
        IResoniteLinkClient importClient,
        string displayName,
        ResoniteTerrainGridGeometry geometry,
        ITextureImportSource heightTextureSource,
        ResoniteFloat2? uvScale,
        ResoniteFloat2? uvOffset,
        CancellationToken cancellationToken)
    {
        PlateauDiagnostics.Verbose(
            "Terrain grid '{Width}x{Height}' importing displacement texture via raw payload.",
            geometry.Width,
            geometry.Height);
        Uri textureUri = await importClient.ImportTextureAsync(heightTextureSource, cancellationToken);
        PlateauDiagnostics.Verbose(
            "Terrain grid '{DisplayName}' displacement texture import completed -> '{TextureUri}'.",
            displayName,
            textureUri);
        return new UploadedTerrainGridAssetBatch(
            geometry,
            textureUri,
            uvScale,
            uvOffset);
    }

    internal static Dictionary<string, Member> CreateTerrainGridTextureMembers(Uri assetUri)
    {
        return new Dictionary<string, Member>(StringComparer.Ordinal)
        {
            ["Readable"] = new Field_bool { Value = true },
            ["Uncompressed"] = new Field_bool { Value = true },
            ["DirectLoad"] = new Field_bool { Value = true },
            ["MipMaps"] = new Field_bool { Value = false },
            ["URL"] = new Field_Uri
            {
                Value = assetUri,
            },
            ["WrapModeU"] = new Field_Enum { Value = "Clamp" },
            ["WrapModeV"] = new Field_Enum { Value = "Clamp" },
            ["FilterMode"] = new Field_Nullable_Enum { Value = "Point" },
        };
    }
}

internal sealed record UploadedTriangleMeshAssetBatch(
    Uri MeshUri);

internal sealed record UploadedTerrainGridAssetBatch(
    ResoniteTerrainGridGeometry Geometry,
    Uri HeightTextureUri,
    ResoniteFloat2? UvScale,
    ResoniteFloat2? UvOffset);
