using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Diagnostics;

using PlateauResoniteLink.Transport.ResoniteLink;
using PlateauResoniteLink.Application.Importing;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal interface IResoniteGeometryAssetAssembler
{
    Task<UploadedTriangleMeshAssetBatch> PrepareTriangleMeshAsync(
        IResoniteLinkClient importClient,
        string meshAssetSlotName,
        string displayName,
        IGeometryImportSource meshSource,
        ILogger logger,
        CancellationToken cancellationToken);

    Task<UploadedTerrainGridAssetBatch> PrepareTerrainGridAsync(
        IResoniteLinkClient importClient,
        string meshAssetSlotName,
        string heightMapAssetSlotName,
        string displayName,
        ResoniteTerrainGridGeometry geometry,
        ITextureImportSource heightTextureSource,
        ResoniteFloat2? uvScale,
        ResoniteFloat2? uvOffset,
        ILogger logger,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteGeometryAssetAssembler : IResoniteGeometryAssetAssembler
{
    public async Task<UploadedTriangleMeshAssetBatch> PrepareTriangleMeshAsync(
        IResoniteLinkClient importClient,
        string meshAssetSlotName,
        string displayName,
        IGeometryImportSource meshSource,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.WriteDebug(
            "Mesh '{DisplayName}' importing triangle mesh (vertices={VertexCount}, submeshes={SubmeshCount}).",
            displayName,
            meshSource.VertexCount,
            meshSource.SubmeshCount);
        Uri assetUri = await importClient.ImportMeshAsync(meshSource, cancellationToken);
        logger.WriteDebug(
            "Mesh '{DisplayName}' mesh import completed -> '{AssetUri}'.",
            displayName,
            assetUri);
        return new UploadedTriangleMeshAssetBatch(meshAssetSlotName, assetUri);
    }

    public async Task<UploadedTerrainGridAssetBatch> PrepareTerrainGridAsync(
        IResoniteLinkClient importClient,
        string meshAssetSlotName,
        string heightMapAssetSlotName,
        string displayName,
        ResoniteTerrainGridGeometry geometry,
        ITextureImportSource heightTextureSource,
        ResoniteFloat2? uvScale,
        ResoniteFloat2? uvOffset,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.WriteDebug(
            "Terrain grid '{Width}x{Height}' importing displacement texture via raw payload.",
            geometry.Width,
            geometry.Height);
        Uri textureUri = await importClient.ImportTextureAsync(heightTextureSource, cancellationToken);
        logger.WriteDebug(
            "Terrain grid '{DisplayName}' displacement texture import completed -> '{TextureUri}'.",
            displayName,
            textureUri);
        return new UploadedTerrainGridAssetBatch(
            meshAssetSlotName,
            heightMapAssetSlotName,
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

internal abstract record UploadedGeometryAssetBatch(string MeshAssetSlotName);

internal sealed record UploadedTriangleMeshAssetBatch(
    string MeshAssetSlotName,
    Uri MeshUri) : UploadedGeometryAssetBatch(MeshAssetSlotName);

internal sealed record UploadedTerrainGridAssetBatch(
    string MeshAssetSlotName,
    string TerrainGridAssetSlotName,
    ResoniteTerrainGridGeometry Geometry,
    Uri HeightTextureUri,
    ResoniteFloat2? UvScale,
    ResoniteFloat2? UvOffset) : UploadedGeometryAssetBatch(MeshAssetSlotName);
