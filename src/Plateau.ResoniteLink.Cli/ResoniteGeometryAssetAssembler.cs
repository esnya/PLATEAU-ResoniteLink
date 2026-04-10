using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal sealed class ResoniteGeometryAssetAssembler(
    Func<IResoniteLinkClient, string, string, ResoniteFloat3?, ResoniteFloatQ?, CancellationToken, Task<ResoniteLinkSceneBuilder.CreatedSlot>> createSlotAsync,
    Func<IResoniteLinkClient, string, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task<ResoniteLinkSceneBuilder.CreatedComponent>> createComponentAsync,
    Action<string>? progressReporter = null)
{
    public async Task<GeometryAssetBuildResult> CreateTriangleMeshAsync(
        IResoniteLinkClient mutationClient,
        IResoniteLinkClient importClient,
        string assetLodSlotId,
        string meshAssetSlotName,
        string displayName,
        ImportMeshRawData meshImport,
        CancellationToken cancellationToken)
    {
        ReportProgress(
            $"[live] Mesh '{displayName}' importing triangle mesh "
            + $"(vertices={meshImport.VertexCount}, submeshes={meshImport.Submeshes.Count}).");
        Uri assetUri = await importClient.ImportMeshAsync(meshImport, cancellationToken);
        ReportProgress($"[live] Mesh '{displayName}' mesh import completed -> '{assetUri}'.");

        ResoniteLinkSceneBuilder.CreatedSlot meshAssetSlot = await createSlotAsync(
            mutationClient,
            assetLodSlotId,
            meshAssetSlotName,
            null,
            null,
            cancellationToken);
        ResoniteLinkSceneBuilder.CreatedComponent geometryComponent = await createComponentAsync(
            mutationClient,
            meshAssetSlot.SlotId,
            "[FrooxEngine]FrooxEngine.StaticMesh",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["URL"] = new Field_Uri
                {
                    Value = assetUri,
                },
            },
            cancellationToken);
        return new GeometryAssetBuildResult(geometryComponent, meshAssetSlot, null);
    }

    public async Task<GeometryAssetBuildResult> CreateHeightMapGridAsync(
        IResoniteLinkClient mutationClient,
        IResoniteLinkClient importClient,
        string assetLodSlotId,
        string cityObjectSlotId,
        string meshAssetSlotName,
        string heightMapAssetSlotName,
        string displayName,
        ResoniteHeightMapGridGeometry geometry,
        ResoniteRawHdrTextureImport heightTextureImport,
        CancellationToken cancellationToken)
    {
        ReportProgress(
            $"[live] HeightMap '{geometry.Width}x{geometry.Height}' importing displacement texture via raw payload.");
        Uri textureUri = await importClient.ImportTextureAsync(heightTextureImport, cancellationToken);

        ResoniteLinkSceneBuilder.CreatedSlot meshAssetSlot = await createSlotAsync(
            mutationClient,
            assetLodSlotId,
            meshAssetSlotName,
            null,
            null,
            cancellationToken);
        ResoniteLinkSceneBuilder.CreatedSlot heightMapAssetSlot = await createSlotAsync(
            mutationClient,
            assetLodSlotId,
            heightMapAssetSlotName,
            null,
            null,
            cancellationToken);
        ResoniteLinkSceneBuilder.CreatedComponent heightTexture = await createComponentAsync(
            mutationClient,
            heightMapAssetSlot.SlotId,
            "[FrooxEngine]FrooxEngine.StaticTexture2D",
            CreateHeightMapTextureMembers(textureUri),
            cancellationToken);

        double displacementMagnitude = Math.Max(geometry.MaxHeight - geometry.MinHeight, 0.0);
        ReportProgress(
            $"[live] HeightMap texture ready. Creating GridMesh "
            + $"({geometry.Width}x{geometry.Height}, displacement={displacementMagnitude:F3}).");
        ResoniteLinkSceneBuilder.CreatedComponent gridMesh = await createComponentAsync(
            mutationClient,
            cityObjectSlotId,
            "[FrooxEngine]FrooxEngine.GridMesh",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Points"] = new Field_int2
                {
                    Value = new int2
                    {
                        x = geometry.Width,
                        y = geometry.Height,
                    },
                },
                ["Size"] = new Field_float2
                {
                    Value = new float2
                    {
                        x = (float)geometry.Size.X,
                        y = (float)geometry.Size.Y,
                    },
                },
                ["DisplacementMagnitude"] = new Field_float
                {
                    Value = (float)displacementMagnitude,
                },
                ["DisplacementTexture"] = new Reference
                {
                    TargetID = heightTexture.ComponentId,
                },
            },
            cancellationToken);
        ReportProgress($"[live] HeightMap '{displayName}' GridMesh ready.");
        return new GeometryAssetBuildResult(gridMesh, meshAssetSlot, heightMapAssetSlot);
    }

    private static Dictionary<string, Member> CreateHeightMapTextureMembers(Uri assetUri)
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

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }
}

internal readonly record struct GeometryAssetBuildResult(
    ResoniteLinkSceneBuilder.CreatedComponent GeometryComponent,
    ResoniteLinkSceneBuilder.CreatedSlot MeshAssetSlot,
    ResoniteLinkSceneBuilder.CreatedSlot? HeightMapAssetSlot);
