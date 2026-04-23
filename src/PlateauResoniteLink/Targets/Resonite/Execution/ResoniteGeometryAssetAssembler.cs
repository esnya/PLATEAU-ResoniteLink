using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

    Task<PreparedGeometryAssetBatch> PrepareHeightMapGridAsync(
        IResoniteLinkClient importClient,
        string meshAssetSlotName,
        string heightMapAssetSlotName,
        string displayName,
        ResoniteHeightMapGridGeometry geometry,
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

    public async Task<PreparedGeometryAssetBatch> PrepareHeightMapGridAsync(
        IResoniteLinkClient importClient,
        string meshAssetSlotName,
        string heightMapAssetSlotName,
        string displayName,
        ResoniteHeightMapGridGeometry geometry,
        ResoniteRawHdrTextureImport heightTextureImport,
        ResoniteFloat2? uvScale,
        ResoniteFloat2? uvOffset,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        progressReporter?.Invoke(
            $"[live] HeightMap '{geometry.Width}x{geometry.Height}' importing displacement texture via raw payload.");
        Uri textureUri = await importClient.ImportTextureAsync(heightTextureImport, cancellationToken);
        progressReporter?.Invoke($"[live] HeightMap '{displayName}' displacement texture import completed -> '{textureUri}'.");
        return new PreparedHeightMapGridAssetBatch(
            meshAssetSlotName,
            heightMapAssetSlotName,
            geometry,
            textureUri,
            uvScale,
            uvOffset);
    }

    internal static Dictionary<string, Member> CreateHeightMapTextureMembers(Uri assetUri)
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

internal sealed record PreparedHeightMapGridAssetBatch(
    string MeshAssetSlotName,
    string HeightMapAssetSlotName,
    ResoniteHeightMapGridGeometry Geometry,
    Uri HeightTextureUri,
    ResoniteFloat2? UvScale,
    ResoniteFloat2? UvOffset) : PreparedGeometryAssetBatch(MeshAssetSlotName);
