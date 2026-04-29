using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal interface IResoniteGeometryAssetAssembler
{
    Task<UploadedGeometryAssetBatch> PrepareTriangleMeshAsync(
        IResoniteLinkClient importClient,
        string meshAssetSlotName,
        string displayName,
        ImportMeshRawData meshImport,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);

    Task<UploadedGeometryAssetBatch> PrepareTerrainGridAsync(
        IResoniteLinkClient importClient,
        string meshAssetSlotName,
        string heightMapAssetSlotName,
        string displayName,
        ResoniteTerrainGridGeometry geometry,
        ResoniteRawHdrTextureImport heightTextureImport,
        ResoniteFloat2? uvScale,
        ResoniteFloat2? uvOffset,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteGeometryAssetAssembler : IResoniteGeometryAssetAssembler
{
    public async Task<UploadedGeometryAssetBatch> PrepareTriangleMeshAsync(
        IResoniteLinkClient importClient,
        string meshAssetSlotName,
        string displayName,
        ImportMeshRawData meshImport,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        progressReporter?.Invoke(
            PlateauLog.Debug(
                "live",
                $"Mesh '{displayName}' importing triangle mesh "
                + $"(vertices={meshImport.VertexCount}, submeshes={meshImport.Submeshes.Count})."));
        Uri assetUri = await importClient.ImportMeshAsync(meshImport, cancellationToken);
        progressReporter?.Invoke(PlateauLog.Debug("live", $"Mesh '{displayName}' mesh import completed -> '{assetUri}'."));
        return new UploadedTriangleMeshAssetBatch(meshAssetSlotName, assetUri);
    }

    public async Task<UploadedGeometryAssetBatch> PrepareTerrainGridAsync(
        IResoniteLinkClient importClient,
        string meshAssetSlotName,
        string heightMapAssetSlotName,
        string displayName,
        ResoniteTerrainGridGeometry geometry,
        ResoniteRawHdrTextureImport heightTextureImport,
        ResoniteFloat2? uvScale,
        ResoniteFloat2? uvOffset,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        progressReporter?.Invoke(
            PlateauLog.Debug(
                "live",
                $"Terrain grid '{geometry.Width}x{geometry.Height}' importing displacement texture via raw payload."));
        Uri textureUri = await importClient.ImportTextureAsync(heightTextureImport, cancellationToken);
        progressReporter?.Invoke(PlateauLog.Debug("live", $"Terrain grid '{displayName}' displacement texture import completed -> '{textureUri}'."));
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
