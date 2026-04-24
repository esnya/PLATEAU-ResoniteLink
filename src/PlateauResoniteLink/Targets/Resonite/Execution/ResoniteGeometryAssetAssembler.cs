using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal interface IResoniteGeometryAssetAssembler
{
    Task<PreparedGeometryAssetBatch> PrepareTriangleMeshAsync(
        IResoniteLinkClient importClient,
        string meshAssetSlotName,
        string displayName,
        ImportMeshRawData meshImport,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);

    Task<PreparedGeometryAssetBatch> PrepareTerrainGridAsync(
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
    public async Task<PreparedGeometryAssetBatch> PrepareTriangleMeshAsync(
        IResoniteLinkClient importClient,
        string meshAssetSlotName,
        string displayName,
        ImportMeshRawData meshImport,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        progressReporter?.Invoke(
            $"[live] Mesh '{displayName}' importing triangle mesh "
            + $"(vertices={meshImport.VertexCount}, submeshes={meshImport.Submeshes.Count}).");
        Uri assetUri = await importClient.ImportMeshAsync(meshImport, cancellationToken);
        progressReporter?.Invoke($"[live] Mesh '{displayName}' mesh import completed -> '{assetUri}'.");
        return new PreparedTriangleMeshAssetBatch(meshAssetSlotName, assetUri);
    }

    public async Task<PreparedGeometryAssetBatch> PrepareTerrainGridAsync(
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
            $"[live] Terrain grid '{geometry.Width}x{geometry.Height}' importing displacement texture via raw payload.");
        Uri textureUri = await importClient.ImportTextureAsync(heightTextureImport, cancellationToken);
        progressReporter?.Invoke($"[live] Terrain grid '{displayName}' displacement texture import completed -> '{textureUri}'.");
        return new PreparedTerrainGridAssetBatch(
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

internal abstract record PreparedGeometryAssetBatch(string MeshAssetSlotName);

internal sealed record PreparedTriangleMeshAssetBatch(
    string MeshAssetSlotName,
    Uri MeshUri) : PreparedGeometryAssetBatch(MeshAssetSlotName);

internal sealed record PreparedTerrainGridAssetBatch(
    string MeshAssetSlotName,
    string TerrainGridAssetSlotName,
    ResoniteTerrainGridGeometry Geometry,
    Uri HeightTextureUri,
    ResoniteFloat2? UvScale,
    ResoniteFloat2? UvOffset) : PreparedGeometryAssetBatch(MeshAssetSlotName);
