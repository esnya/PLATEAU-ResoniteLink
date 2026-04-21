using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

[Trait("Category", "Slow")]
public sealed class ResoniteLiveSceneImportTargetYOffsetTests
{
    private const string DatasetName = "tokyo23ku";
    private static readonly ResoniteLocalOrigin LocalOrigin = new(35.6875, 139.69375, 0.0);

    [Fact]
    public async Task BeginAndBuildPreservesCityObjectHeightWithoutDatasetLift()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        ResoniteConstructionMetadata metadata = CreateMetadata(fixturePath, "53394525");
        ResoniteConstructionCityObject cityObject = CreateMeshCityObject(
            displayName: "Height Test Building",
            actualMeshCode: "53394525",
            sourceObjectKey: "height-test-building",
            worldPosition: new ResoniteFloat3(1.0, 15.5, 2.5));
        using SceneBuilderRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.BuildSceneAsync(metadata, [cityObject], client);

        Slot datasetSlot = FindNonAssetSlotByName(client, $"PLATEAU {DatasetName}");
        Slot objectSlot = FindNonAssetSlotByName(client, "Height Test Building");

        Assert.Equal(0.0, GetSlotY(datasetSlot));
        Assert.Equal(15.5, GetSlotY(objectSlot));
    }

    private static ResoniteConstructionMetadata CreateMetadata(
        string fixturePath,
        string meshCode,
        IReadOnlyList<string>? requestedMeshCodes = null)
    {
        return ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            meshCode,
            fixturePath,
            LocalOrigin,
            packageNames: ["bldg", "dem"],
            sourceFiles:
            [
                "udx/dem/533945/plateau_tokyo23ku_dem_533945.gml",
                "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            ],
            requestedMeshCodes: requestedMeshCodes);
    }

    private static ResoniteConstructionCityObject CreateMeshCityObject(
        string displayName,
        string actualMeshCode,
        string sourceObjectKey,
        ResoniteFloat3 worldPosition)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: sourceObjectKey,
            DisplayName: displayName,
            PackageName: "bldg",
            ActualMeshCode: actualMeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(worldPosition),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh("wireframe-material"),
            Materials: [CreateWireframeMaterial()],
            SourceObjectKey: sourceObjectKey);
    }

    private static ResoniteMaterialBinding CreateWireframeMaterial()
    {
        return new ResoniteMaterialBinding(
            MaterialKey: "wireframe-material",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Wireframe,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0]);
    }

    private static double GetSlotY(Slot slot)
    {
        return slot.Position is Field_float3 position
            ? position.Value.y
            : 0.0;
    }

    private static Slot FindNonAssetSlotByName(SceneBuilderRecordingClient client, string slotName)
    {
        return Assert.Single(
            client.SlotsById.Values,
            slot => string.Equals(slot.Name?.Value, slotName, StringComparison.Ordinal)
                && client.SlotPaths.TryGetValue(slot.ID, out string? path)
                && !path.Contains("/Assets/", StringComparison.Ordinal));
    }
}
