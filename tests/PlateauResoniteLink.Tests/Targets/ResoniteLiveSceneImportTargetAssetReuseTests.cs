using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using GeographicLib;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

[Collection(BundledCompanionTextureIsolationGroup.Name)]
[Trait("Category", "Slow")]
public sealed class ResoniteLiveSceneImportTargetAssetReuseTests
{
    private const string DatasetName = "reuse-test";
    private const string MeshCode = "53394525";
    private const string SecondaryMeshCode = "53394526";
    private const string PrimarySourceFile = $"udx/bldg/{MeshCode}/plateau_{DatasetName}_bldg_{MeshCode}.gml";
    private const string SecondarySourceFile = $"udx/bldg/{SecondaryMeshCode}/plateau_{DatasetName}_bldg_{SecondaryMeshCode}.gml";
    private const string PrimaryDemSourceFile = $"udx/dem/{MeshCode}/plateau_{DatasetName}_dem_{MeshCode}.gml";
    private const string SecondaryDemSourceFile = $"udx/dem/{SecondaryMeshCode}/plateau_{DatasetName}_dem_{SecondaryMeshCode}.gml";
    private const string ThirdMeshCode = "53394527";
    private const string ThirdSourceFile = $"udx/bldg/{ThirdMeshCode}/plateau_{DatasetName}_bldg_{ThirdMeshCode}.gml";
    private const string ParentMeshCode = "533945";
    private const string ParentDemSourceFile = $"udx/dem/{ParentMeshCode}/plateau_{DatasetName}_dem_{ParentMeshCode}.gml";
    private static readonly ResoniteLocalOrigin LocalOrigin = new(35.6875, 139.69375, 0.0);

    [Fact]
    public async Task ExecuteAsyncSharesCommonMaterialAssetsAcrossCityObjectsInSameSession()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [
                CreateBundledTriangleCityObject("shared-material-one"),
                CreateBundledTriangleCityObject("shared-material-two"),
            ],
            client,
            enableMeshBake: false);

        string firstMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject shared-material-one");
        string secondMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject shared-material-two");
        string commonMaterialContainerSlotId = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ID, firstMaterialId, StringComparison.Ordinal)).ContainerSlotId;

        Assert.Equal(firstMaterialId, secondMaterialId);
        Assert.StartsWith(
            "PLATEAU Shared Assets/Common Materials/",
            client.SlotPaths[commonMaterialContainerSlotId],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsyncDoesNotShareCommonMaterialAssetsWhenUvScaleDiffers()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [
                CreateBundledTriangleCityObject("shared-material-scale-one"),
                CreateBundledTriangleCityObject("shared-material-scale-two", textureScale: new ResoniteFloat2(0.5, 0.5)),
            ],
            client,
            enableMeshBake: false);

        Assert.NotEqual(
            GetRendererMaterialReferenceTarget(client, "CityObject shared-material-scale-one"),
            GetRendererMaterialReferenceTarget(client, "CityObject shared-material-scale-two"));
    }

    [Fact]
    public async Task ExecuteAsyncSharesBundledTriplanarRoofCommonMaterialAssets()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [
                CreateBundledTriangleCityObject(
                    "shared-roof-one",
                    family: BundledDefaultMaterialFamilies.Roof,
                    projection: ResoniteMaterialProjection.Triplanar),
                CreateBundledTriangleCityObject(
                    "shared-roof-two",
                    family: BundledDefaultMaterialFamilies.Roof,
                    projection: ResoniteMaterialProjection.Triplanar),
            ],
            client,
            enableMeshBake: false);

        string firstMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject shared-roof-one");
        string secondMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject shared-roof-two");
        string commonMaterialContainerSlotId = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ID, firstMaterialId, StringComparison.Ordinal)).ContainerSlotId;

        Assert.Equal(firstMaterialId, secondMaterialId);
        Assert.StartsWith(
            "PLATEAU Shared Assets/Common Materials/",
            client.SlotPaths[commonMaterialContainerSlotId],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsyncReusesSharedCommonMaterialForPayloadAlbedoOverrides()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [
                CreatePayloadTriangleCityObject(
                    "dataset-texture-one",
                    ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(255, 0, 0, "textures/albedo-one.png")),
                CreatePayloadTriangleCityObject(
                    "dataset-texture-two",
                    ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(0, 255, 0, "textures/albedo-two.png")),
            ],
            client,
            enableMeshBake: false);

        string firstMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject dataset-texture-one");
        string secondMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject dataset-texture-two");
        HashSet<string> commonMaterialIds = client.AddedComponents
            .Where(request => client.SlotPaths.TryGetValue(request.ContainerSlotId, out string? path)
                && path.Contains("PLATEAU Shared Assets/Common Materials/", StringComparison.Ordinal))
            .Select(static request => request.Data.ID)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        string firstPropertyBlockId = GetRendererMaterialPropertyBlockReferenceTarget(client, "CityObject dataset-texture-one");
        string secondPropertyBlockId = GetRendererMaterialPropertyBlockReferenceTarget(client, "CityObject dataset-texture-two");

        Assert.Equal(firstMaterialId, secondMaterialId);
        Assert.Contains(firstMaterialId, commonMaterialIds);
        Assert.NotEqual(firstPropertyBlockId, secondPropertyBlockId);
        Assert.Contains(client.ImportedRawTextures, static texture => IsSolidColorTexture(texture, 255, 0, 0));
        Assert.Contains(client.ImportedRawTextures, static texture => IsSolidColorTexture(texture, 0, 255, 0));
    }

    [Fact]
    public async Task ExecuteAsyncReusesSharedCommonMaterialForPayloadAlbedoOverridesWithExplicitNoOpTransform()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [
                CreatePayloadTriangleCityObject(
                    "dataset-texture-noop-transform-one",
                    ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(255, 0, 0, "textures/albedo-noop-one.png"),
                    textureScale: new ResoniteFloat2(1.0, 1.0),
                    textureOffset: new ResoniteFloat2(0.0, 0.0)),
                CreatePayloadTriangleCityObject(
                    "dataset-texture-noop-transform-two",
                    ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(0, 255, 0, "textures/albedo-noop-two.png")),
            ],
            client,
            enableMeshBake: false);

        string firstMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject dataset-texture-noop-transform-one");
        string secondMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject dataset-texture-noop-transform-two");

        Assert.Equal(firstMaterialId, secondMaterialId);
        Assert.Equal(1, CountCommonMaterialComponents(client, firstMaterialId));
    }

    [Fact]
    public async Task ExecuteAsyncReusesSharedCommonMaterialForPayloadAlbedoOverridesWithDifferentUvTransforms()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [
                CreatePayloadTriangleCityObject(
                    "dataset-texture-scaled-one",
                    ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(255, 0, 0, "textures/albedo-scaled-one.png"),
                    textureScale: new ResoniteFloat2(0.5, 0.25),
                    textureOffset: new ResoniteFloat2(0.125, 0.75)),
                CreatePayloadTriangleCityObject(
                    "dataset-texture-scaled-two",
                    ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(0, 255, 0, "textures/albedo-scaled-two.png"),
                    textureScale: new ResoniteFloat2(2.0, 1.5),
                    textureOffset: new ResoniteFloat2(0.25, 0.5)),
            ],
            client,
            enableMeshBake: false);

        string firstMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject dataset-texture-scaled-one");
        string secondMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject dataset-texture-scaled-two");
        HashSet<string> commonMaterialIds = client.AddedComponents
            .Where(request => client.SlotPaths.TryGetValue(request.ContainerSlotId, out string? path)
                && path.Contains("PLATEAU Shared Assets/Common Materials/", StringComparison.Ordinal))
            .Select(static request => request.Data.ID)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(firstMaterialId, secondMaterialId);
        Assert.Contains(firstMaterialId, commonMaterialIds);
        Assert.True(client.ImportedMeshes.Count >= 2);
        HashSet<string> importedUvSignatures = client.ImportedMeshes
            .Select(CreateMeshUvSignature)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(
            CreateMeshUvSignature(
                new ResoniteFloat2(0.125, 0.75),
                new ResoniteFloat2(0.625, 0.75),
                new ResoniteFloat2(0.125, 1.0)),
            importedUvSignatures);
        Assert.Contains(
            CreateMeshUvSignature(
                new ResoniteFloat2(0.25, 0.5),
                new ResoniteFloat2(2.25, 0.5),
                new ResoniteFloat2(0.25, 2.0)),
            importedUvSignatures);
    }

    [Fact]
    public async Task ExecuteAsyncSetsUpFixedGenericAndVertexColorCommonMaterials()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [CreateBundledTriangleCityObject("setup-common-check")],
            client);

        Slot commonRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(client, "PLATEAU Shared Assets/Common Materials");

        Assert.Contains(
            client.SlotsById.Values,
            slot => string.Equals(slot.Name?.Value, "uv", StringComparison.Ordinal)
                && slot.Parent is not null
                && ResoniteLiveSceneImportTargetTestSupport.IsDescendantOf(client, slot.ID, commonRoot.ID));
        Assert.Contains(
            client.SlotsById.Values,
            slot => string.Equals(slot.Name?.Value, "uv", StringComparison.Ordinal)
                && slot.Parent is not null
                && client.SlotPaths[slot.ID!].Replace('\\', '/').Contains("/vertex-color/", StringComparison.Ordinal)
                && slot.Parent is not null
                && ResoniteLiveSceneImportTargetTestSupport.IsDescendantOf(client, slot.ID, commonRoot.ID));
    }

    [Fact]
    public async Task ExecuteAsyncCreatesBundledCommonMaterialSlotAndComponentsInSameBatch()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [CreateBundledTriangleCityObject("bundled-common-batch")],
            client);

        IReadOnlyList<DataModelOperation>[] batches = client.Batches.ToArray();
        int bundledMaterialBatchIndex = Array.FindIndex(
            batches,
            batch => batch.OfType<AddSlot>().Any(static operation =>
                string.Equals(operation.Data.Name?.Value, "variant-0", StringComparison.Ordinal)));

        Assert.True(bundledMaterialBatchIndex >= 0, "Expected bundled common material slot creation batch.");
        Assert.DoesNotContain(
            batches.Take(bundledMaterialBatchIndex).SelectMany(static batch => batch).OfType<AddSlot>(),
            static operation => string.Equals(operation.Data.Name?.Value, "variant-0", StringComparison.Ordinal));
        Assert.Contains(
            batches[bundledMaterialBatchIndex].OfType<AddComponent>(),
            static operation => string.Equals(operation.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));
        Assert.Contains(
            batches[bundledMaterialBatchIndex].OfType<AddComponent>(),
            static operation => string.Equals(operation.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsyncReusesSharedCommonMaterialForVertexColor()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [
                CreateVertexColorTriangleCityObject("vertex-color-one"),
                CreateVertexColorTriangleCityObject("vertex-color-two"),
            ],
            client,
            enableMeshBake: false);

        string firstMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject vertex-color-one");
        string secondMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject vertex-color-two");
        string commonMaterialContainerSlotId = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ID, firstMaterialId, StringComparison.Ordinal)).ContainerSlotId;

        Assert.Equal(firstMaterialId, secondMaterialId);
        Assert.Contains("/vertex-color/", client.SlotPaths[commonMaterialContainerSlotId], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsyncReusesExistingSharedVertexColorCommonMaterialAssetsAcrossRuns()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneTwiceAsync(
            metadata,
            [CreateVertexColorTriangleCityObject("vertex-color-run-one")],
            [CreateVertexColorTriangleCityObject("vertex-color-run-two")],
            client);

        string firstMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject vertex-color-run-one");
        string secondMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject vertex-color-run-two");

        Assert.Equal(firstMaterialId, secondMaterialId);
        Assert.Equal(1, CountCommonMaterialComponents(client, firstMaterialId));
    }

    [Fact]
    public async Task ExecuteAsyncReusesExistingEmptyCurrentGenericCommonMaterialSlot()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        string emptyCurrentMaterialSlotId = await SeedEmptyCurrentGenericSharedMaterialSlotAsync(client);

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [
                CreatePayloadTriangleCityObject(
                    "empty-current-generic-slot-reuse",
                    ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(255, 0, 0, "textures/empty-current-generic-slot.png")),
            ],
            client,
            enableMeshBake: false);

        string rendererMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject empty-current-generic-slot-reuse");
        string currentGenericPath = Assert.Single(
            client.SlotsById.Values,
            slot => string.Equals(slot.Name?.Value, "uv", StringComparison.Ordinal)
                && client.SlotPaths[slot.ID!].Replace('\\', '/').EndsWith("/generic/uv", StringComparison.Ordinal)).ID!;
        AddComponent materialComponentRequest = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ID, rendererMaterialId, StringComparison.Ordinal));

        Assert.Equal(emptyCurrentMaterialSlotId, currentGenericPath);
        Assert.Equal(emptyCurrentMaterialSlotId, materialComponentRequest.ContainerSlotId);
        Assert.Equal(
            1,
            client.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, "uv", StringComparison.Ordinal)
                && client.SlotPaths[slot.ID!].Replace('\\', '/').EndsWith("/generic/uv", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ExecuteAsyncRejectsIncompleteCommonMaterialSlotWithNonMaterialComponents()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        string incompleteMaterialSlotId = await SeedEmptyCurrentGenericSharedMaterialSlotAsync(client);
        _ = await client.AddComponentAsync(
            new AddComponent
            {
                ContainerSlotId = incompleteMaterialSlotId,
                Data = new Component
                {
                    ComponentType = "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    Members = [],
                },
            },
            CancellationToken.None);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
                metadata,
                [
                    CreatePayloadTriangleCityObject(
                        "incomplete-current-generic-slot",
                        ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(255, 0, 0, "textures/incomplete-current-generic-slot.png")),
                ],
                client,
                enableMeshBake: false));

        Assert.Contains("exists but does not contain material component", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsyncReadsExistingSharedMaterialAssetsWithTargetedGetSlotDepth()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        string emptyCurrentMaterialSlotId = await SeedEmptyCurrentGenericSharedMaterialSlotAsync(client);

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [
                CreatePayloadTriangleCityObject(
                    "targeted-shared-material-read",
                    ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(255, 0, 0, "textures/targeted-shared-material-read.png")),
            ],
            client,
            enableMeshBake: false);

        string rendererMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject targeted-shared-material-read");
        AddComponent materialComponentRequest = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ID, rendererMaterialId, StringComparison.Ordinal));
        Assert.Equal(emptyCurrentMaterialSlotId, materialComponentRequest.ContainerSlotId);
        SlotGetRequest[] rootGetSlotCalls = client.SlotGetRequests
            .Where(static request => string.Equals(request.SlotPath, "Root", StringComparison.Ordinal))
            .ToArray();
        SlotGetRequest[] sharedAssetsGetSlotCalls = client.SlotGetRequests
            .Where(static request => string.Equals(request.SlotPath, "PLATEAU Shared Assets", StringComparison.Ordinal))
            .ToArray();
        SlotGetRequest[] commonMaterialsGetSlotCalls = client.SlotGetRequests
            .Where(static request => string.Equals(request.SlotPath, "PLATEAU Shared Assets/Common Materials", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(rootGetSlotCalls);
        Assert.NotEmpty(sharedAssetsGetSlotCalls);
        Assert.NotEmpty(commonMaterialsGetSlotCalls);
        Assert.All(rootGetSlotCalls, request => Assert.Equal(1, request.Depth));
        Assert.All(sharedAssetsGetSlotCalls, request => Assert.Equal(1, request.Depth));
        Assert.All(commonMaterialsGetSlotCalls, request => Assert.Equal(2, request.Depth));
    }

    [Fact]
    public async Task ExecuteAsyncReusesSharedCommonMaterialAcrossRunsForPayloadAlbedoOverridesWithDifferentUvTransforms()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneTwiceAsync(
            metadata,
            [
                CreatePayloadTriangleCityObject(
                    "dataset-texture-scaled-run-one",
                    ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(255, 0, 0, "textures/albedo-scaled-run-one.png"),
                    textureScale: new ResoniteFloat2(0.5, 0.25),
                    textureOffset: new ResoniteFloat2(0.125, 0.75)),
            ],
            [
                CreatePayloadTriangleCityObject(
                    "dataset-texture-scaled-run-two",
                    ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(0, 255, 0, "textures/albedo-scaled-run-two.png"),
                    textureScale: new ResoniteFloat2(2.0, 1.5),
                    textureOffset: new ResoniteFloat2(0.25, 0.5)),
            ],
            client,
            enableMeshBake: false);

        string firstMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject dataset-texture-scaled-run-one");
        string secondMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject dataset-texture-scaled-run-two");
        HashSet<string> importedUvSignatures = client.ImportedMeshes
            .Select(CreateMeshUvSignature)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(firstMaterialId, secondMaterialId);
        Assert.Equal(1, CountCommonMaterialComponents(client, firstMaterialId));
        Assert.Contains(
            CreateMeshUvSignature(
                new ResoniteFloat2(0.125, 0.75),
                new ResoniteFloat2(0.625, 0.75),
                new ResoniteFloat2(0.125, 1.0)),
            importedUvSignatures);
        Assert.Contains(
            CreateMeshUvSignature(
                new ResoniteFloat2(0.25, 0.5),
                new ResoniteFloat2(2.25, 0.5),
                new ResoniteFloat2(0.25, 2.0)),
            importedUvSignatures);
    }

    [Fact]
    public async Task ExecuteAsyncUsesDistinctPropertyBlocksForSamePresentationMaterialWithDifferentPayloadOverrides()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [
                CreateSameKeyPayloadOverrideCityObject(
                    "same-material-override",
                    ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(255, 0, 0, "textures/same-material-a.png"),
                    ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(0, 255, 0, "textures/same-material-b.png")),
            ],
            client,
            enableMeshBake: false);

        string[] materialIds = GetRendererMaterialReferenceTargets(client, "CityObject same-material-override");
        string?[] propertyBlockIds = GetRendererMaterialPropertyBlockReferenceTargets(client, "CityObject same-material-override");

        Assert.Equal(2, materialIds.Length);
        Assert.All(materialIds, materialId => Assert.Equal(materialIds[0], materialId));
        Assert.Equal(2, propertyBlockIds.Length);
        Assert.All(propertyBlockIds, static propertyBlockId => Assert.False(string.IsNullOrWhiteSpace(propertyBlockId)));
        Assert.Equal(2, propertyBlockIds.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ImportAsyncPreservesMixedCommonAndOverrideMaterialOrder()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [
                CreateMixedMaterialCityObject(
                    "mixed-material-order",
                    ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(255, 0, 0, "textures/mixed-albedo.png")),
            ],
            client,
            enableMeshBake: false);

        string[] materialIds = GetRendererMaterialReferenceTargets(client, "CityObject mixed-material-order");
        Assert.Equal(2, materialIds.Length);
        HashSet<string> commonMaterialIds = client.AddedComponents
            .Where(request => client.SlotPaths.TryGetValue(request.ContainerSlotId, out string? path)
                && path.Contains("PLATEAU Shared Assets/Common Materials/", StringComparison.Ordinal))
            .Select(static request => request.Data.ID)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        string?[] propertyBlockIds = GetRendererMaterialPropertyBlockReferenceTargets(client, "CityObject mixed-material-order");

        Assert.Contains(materialIds[0], commonMaterialIds);
        Assert.Contains(materialIds[1], commonMaterialIds);
        Assert.Null(propertyBlockIds[0]);
        Assert.NotNull(propertyBlockIds[1]);
        Assert.Contains(client.ImportedRawTextures, static texture => IsSolidColorTexture(texture, 255, 0, 0));
    }

    [Fact]
    public async Task ExecuteAsyncReusesNamedDatasetRootAssetsAndCommonAcrossRuns()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneTwiceAsync(
            metadata,
            [CreateBundledTriangleCityObject("reuse-run-one")],
            [CreateBundledTriangleCityObject("reuse-run-two")],
            client);

        Slot datasetRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, $"PLATEAU {DatasetName}");
        Slot assetsRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(client, $"PLATEAU {DatasetName}/Assets");
        Slot sharedAssetsRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(client, "PLATEAU Shared Assets");
        Slot commonRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(client, "PLATEAU Shared Assets/Common Materials");

        Assert.Equal(datasetRoot.ID, assetsRoot.Parent?.TargetID);
        Assert.Equal(sharedAssetsRoot.ID, commonRoot.Parent?.TargetID);
        Assert.True(ResoniteLiveSceneImportTargetTestSupport.IsDescendantOf(client, commonRoot.ID, sharedAssetsRoot.ID));
        Assert.True(client.ImportedMeshes.Count >= 2);
    }

    [Fact]
    public async Task ExecuteAsyncReusesExistingSharedCommonMaterialAssetsForPayloadOverridesAcrossRuns()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using SceneSinkRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneTwiceAsync(
            metadata,
            [
                CreatePayloadTriangleCityObject(
                    "payload-run-one",
                    ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(255, 0, 0, "textures/payload-run-one.png")),
            ],
            [
                CreatePayloadTriangleCityObject(
                    "payload-run-two",
                    ResoniteLiveSceneImportTargetTestSupport.CreateSolidColorPayload(0, 255, 0, "textures/payload-run-two.png")),
            ],
            client);

        string firstMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject payload-run-one");
        string secondMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject payload-run-two");

        Assert.Equal(firstMaterialId, secondMaterialId);
        Assert.Equal(1, CountCommonMaterialComponents(client, firstMaterialId));
    }

    [Fact]
    public async Task ExecuteAsyncAssignsSourceFileRootPositionForNonCompletionMeshAndPreservesWorldPosition()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path, [PrimarySourceFile, SecondarySourceFile]);
        using SceneSinkRecordingClient client = new();
        ResoniteFloat3 worldPosition = new(123.0, 0.0, 456.0);

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [
                CreateBundledTriangleCityObject(
                    "offset-run-one",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondarySourceFile,
                    worldPosition: worldPosition),
            ],
            client);

        Slot sourceFileRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(SecondarySourceFile)}");
        ResoniteFloat3 expectedRootOffset = ComputeMeshCodeOffset(MeshCode, SecondaryMeshCode);

        Assert.Equal(expectedRootOffset.X, GetSlotPosition(sourceFileRoot).X, 3);
        Assert.Equal(expectedRootOffset.Z, GetSlotPosition(sourceFileRoot).Z, 3);

        Slot objectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject offset-run-one");
        ResoniteFloat3 accumulatedPosition = GetAccumulatedPosition(client, objectSlot);
        Assert.Equal(worldPosition.X, accumulatedPosition.X, 3);
        Assert.Equal(worldPosition.Y, accumulatedPosition.Y, 3);
        Assert.Equal(worldPosition.Z, accumulatedPosition.Z, 3);
    }

    [Fact]
    public async Task ExecuteAsyncCreatesIndependentSourceFileRootAcrossRuns()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path, [PrimarySourceFile, SecondarySourceFile]);
        using SceneSinkRecordingClient client = new();
        ResoniteFloat3 secondRunWorldPosition = new(200.0, 0.0, 300.0);

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneTwiceAsync(
            metadata,
            [
                CreateBundledTriangleCityObject(
                    "offset-run-one",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondarySourceFile,
                    worldPosition: new ResoniteFloat3(123.0, 0.0, 456.0)),
            ],
            [
                CreateBundledTriangleCityObject(
                    "offset-run-two",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondarySourceFile,
                    worldPosition: secondRunWorldPosition),
            ],
            client);

        string sourceFileRootName = Path.GetFileNameWithoutExtension(SecondarySourceFile);
        Slot[] sourceFileRoots = ResoniteLiveSceneImportTargetTestSupport.FindSlotsByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{sourceFileRootName}");
        ResoniteFloat3 expectedRootOffset = ComputeMeshCodeOffset(MeshCode, SecondaryMeshCode);

        Assert.Equal(2, sourceFileRoots.Length);
        Assert.All(
            sourceFileRoots,
            slot =>
            {
                ResoniteFloat3 position = GetSlotPosition(slot);
                Assert.Equal(expectedRootOffset.X, position.X, 3);
                Assert.Equal(expectedRootOffset.Z, position.Z, 3);
            });

        Slot objectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject offset-run-two");
        ResoniteFloat3 accumulatedPosition = GetAccumulatedPosition(client, objectSlot);
        AssertNear(secondRunWorldPosition, accumulatedPosition, 0.2);
    }

    [Fact]
    public async Task ExecuteAsyncCreatesNewSourceFileRootWithoutMutatingExistingVerticalOffset()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path, [PrimarySourceFile, SecondarySourceFile]);
        using SceneSinkRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneTwiceAsync(
            metadata,
            [
                CreateBundledTriangleCityObject(
                    "offset-y-run-one",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondarySourceFile,
                    worldPosition: new ResoniteFloat3(10.0, 3.0, 20.0)),
            ],
            [],
            client);

        string sourceFileRootName = Path.GetFileNameWithoutExtension(SecondarySourceFile);
        Slot sourceFileRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{sourceFileRootName}");
        sourceFileRoot.Position = new Field_float3
        {
            Value = new float3
            {
                x = sourceFileRoot.Position!.Value.x,
                y = 12.5f,
                z = sourceFileRoot.Position.Value.z,
            },
        };

        using TemporaryDirectory workDirectory = new();
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client);
        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [
                CreateBundledTriangleCityObject(
                    "offset-y-run-two",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondarySourceFile,
                    worldPosition: new ResoniteFloat3(30.0, 15.0, 40.0)),
            ]);

        Slot[] sourceFileRoots = ResoniteLiveSceneImportTargetTestSupport.FindSlotsByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{sourceFileRootName}");
        Assert.Equal(2, sourceFileRoots.Length);
        Assert.Equal(12.5, GetSlotPosition(sourceFileRoots[0]).Y, 3);
        Assert.Equal(12.5, GetSlotPosition(sourceFileRoots[1]).Y, 3);

        Slot objectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject offset-y-run-two");
        AssertNear(new ResoniteFloat3(30.0, 15.0, 40.0), GetAccumulatedPosition(client, objectSlot), 0.2);
    }

    [Fact]
    public async Task ExecuteAsyncReusesExistingLod0BranchForNullLodObjects()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path, [SecondarySourceFile]);
        using SceneSinkRecordingClient client = new();

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneTwiceAsync(
            metadata,
            [
                CreateBundledTriangleCityObject(
                    "lod0-first",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondarySourceFile,
                    worldPosition: new ResoniteFloat3(10.0, 0.0, 20.0),
                    lodLevel: 0),
            ],
            [
                CreateBundledTriangleCityObject(
                    "lod0-second",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondarySourceFile,
                    worldPosition: new ResoniteFloat3(11.0, 0.0, 21.0),
                    lodLevel: null),
            ],
            client);

        string sourceFileRootName = Path.GetFileNameWithoutExtension(SecondarySourceFile);
        Slot[] datasetSourceFileRoots = ResoniteLiveSceneImportTargetTestSupport.FindSlotsByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{sourceFileRootName}");

        Assert.Equal(2, datasetSourceFileRoots.Length);
        Assert.All(
            datasetSourceFileRoots,
            datasetSourceFileRoot =>
            {
                Assert.Equal(
                    1,
                    client.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, "LOD0", StringComparison.Ordinal)
                        && string.Equals(slot.Parent?.TargetID, datasetSourceFileRoot.ID, StringComparison.Ordinal)));
                Assert.DoesNotContain(
                    client.SlotsById.Values,
                    slot => string.Equals(slot.Name?.Value, "LOD", StringComparison.Ordinal)
                        && string.Equals(slot.Parent?.TargetID, datasetSourceFileRoot.ID, StringComparison.Ordinal));
            });
    }

    [Fact]
    public async Task ExecuteAsyncAssignsSourceFileRootPositionForTerrainGridDemAndPreservesWorldPosition()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateDemMetadata(datasetDirectory.Path, [PrimaryDemSourceFile, SecondaryDemSourceFile]);
        using SceneSinkRecordingClient client = new();
        ResoniteFloat3 worldPosition = new(123.0, 15.5, 456.0);

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [
                CreateTerrainGridDemCityObject(
                    "dem-terrain-grid-run-one",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondaryDemSourceFile,
                    worldPosition: worldPosition),
            ],
            client);

        Slot sourceFileRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(SecondaryDemSourceFile)}");
        ResoniteFloat3 expectedRootOffset = ComputeMeshCodeOffset(MeshCode, SecondaryMeshCode);

        Assert.Equal(expectedRootOffset.X, GetSlotPosition(sourceFileRoot).X, 3);
        Assert.Equal(expectedRootOffset.Z, GetSlotPosition(sourceFileRoot).Z, 3);

        Slot objectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "DEM Terrain Grid dem-terrain-grid-run-one");
        ResoniteFloat3 accumulatedPosition = GetAccumulatedPosition(client, objectSlot);
        Assert.Equal(worldPosition.X, accumulatedPosition.X, 3);
        Assert.Equal(worldPosition.Y, accumulatedPosition.Y, 3);
        Assert.Equal(worldPosition.Z, accumulatedPosition.Z, 3);
    }

    [Fact]
    public async Task ExecuteAsyncCreatesIndependentSourceFileRootAcrossRunsForTerrainGridDem()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateDemMetadata(datasetDirectory.Path, [PrimaryDemSourceFile, SecondaryDemSourceFile]);
        using SceneSinkRecordingClient client = new();
        ResoniteFloat3 secondRunWorldPosition = new(200.0, 25.0, 300.0);

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneTwiceAsync(
            metadata,
            [
                CreateTerrainGridDemCityObject(
                    "dem-terrain-grid-run-one",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondaryDemSourceFile,
                    worldPosition: new ResoniteFloat3(123.0, 15.5, 456.0)),
            ],
            [
                CreateTerrainGridDemCityObject(
                    "dem-terrain-grid-run-two",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondaryDemSourceFile,
                    worldPosition: secondRunWorldPosition),
            ],
            client);

        string sourceFileRootName = Path.GetFileNameWithoutExtension(SecondaryDemSourceFile);
        Slot[] sourceFileRoots = ResoniteLiveSceneImportTargetTestSupport.FindSlotsByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{sourceFileRootName}");
        ResoniteFloat3 expectedRootOffset = ComputeMeshCodeOffset(MeshCode, SecondaryMeshCode);

        Assert.Equal(2, sourceFileRoots.Length);
        Assert.All(
            sourceFileRoots,
            slot =>
            {
                ResoniteFloat3 position = GetSlotPosition(slot);
                Assert.Equal(expectedRootOffset.X, position.X, 3);
                Assert.Equal(expectedRootOffset.Z, position.Z, 3);
            });

        Slot objectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "DEM Terrain Grid dem-terrain-grid-run-two");
        ResoniteFloat3 accumulatedPosition = GetAccumulatedPosition(client, objectSlot);
        AssertNear(secondRunWorldPosition, accumulatedPosition, 0.2);
    }

    [Fact]
    public async Task ExecuteAsyncAppendPlacesBldgFromExistingParentSourceRoot()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata firstRunMetadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            ParentMeshCode,
            datasetDirectory.Path,
            RequireMeshCodeCenter(ParentMeshCode),
            packageNames: ["dem"],
            sourceFiles: [ParentDemSourceFile]);
        ImportedSceneMetadata secondRunMetadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            RequireMeshCodeCenter(MeshCode),
            packageNames: ["dem", "bldg"],
            sourceFiles: [ParentDemSourceFile, PrimarySourceFile]);
        using SceneSinkRecordingClient client = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();
        ResoniteFloat3 firstRunDemWorldPosition = new(250.0, 4.0, 400.0);
        ResoniteFloat3 secondRunDemWorldPosition = new(230.0, 9.0, 390.0);
        ResoniteFloat3 sourceRootCorrection = new(16.5, 0.0, -47.25);

        await using (ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client))
        {
            _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
                importTarget,
                firstRunMetadata,
                firstWorkDirectory.Path,
                [
                    CreateTerrainGridDemCityObject(
                        "parent-existing",
                        actualMeshCode: MeshCode,
                        sourceFileRelativePath: ParentDemSourceFile,
                        worldPosition: firstRunDemWorldPosition),
                ]);
        }

        Slot existingDemSourceRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(ParentDemSourceFile)}");
        ResoniteFloat3 existingDemRootPosition = Add(GetSlotPosition(existingDemSourceRoot), sourceRootCorrection);
        existingDemSourceRoot.Position = CreateFloat3(existingDemRootPosition);

        await using (ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client))
        {
            _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
                importTarget,
                secondRunMetadata,
                secondWorkDirectory.Path,
                [
                    CreateTerrainGridDemCityObject(
                        "parent-appended",
                        actualMeshCode: MeshCode,
                        sourceFileRelativePath: ParentDemSourceFile,
                        worldPosition: secondRunDemWorldPosition),
                    CreateBundledTriangleCityObject(
                        "bldg-appended",
                        actualMeshCode: MeshCode,
                        sourceFileRelativePath: PrimarySourceFile,
                        worldPosition: secondRunDemWorldPosition),
                ]);
        }

        Slot appendedDemSourceRoot = ResoniteLiveSceneImportTargetTestSupport.FindSlotsByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(ParentDemSourceFile)}")
            .Single(slot => !string.Equals(slot.ID, existingDemSourceRoot.ID, StringComparison.Ordinal));
        Slot bldgSourceRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(PrimarySourceFile)}");
        ResoniteFloat3 parentToChildOffset = ComputeMeshCodeOffset(ParentMeshCode, MeshCode);
        ResoniteFloat3 expectedBldgRootPosition = Add(existingDemRootPosition, parentToChildOffset);

        AssertNear(existingDemRootPosition, GetSlotPosition(appendedDemSourceRoot), 0.2);
        AssertNear(expectedBldgRootPosition, GetSlotPosition(bldgSourceRoot), 0.2);
    }

    [Fact]
    public async Task ExecuteAsyncRepeatedBldgAppendUsesExactObservedRootPosition()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata bldgMetadata = CreateMetadata(datasetDirectory.Path, [PrimarySourceFile]);
        using SceneSinkRecordingClient client = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();
        ResoniteFloat3 bldgWorldPosition = new(100.0, 9.0, 100.0);

        await ExecuteImportAsync(
            client,
            bldgMetadata,
            firstWorkDirectory.Path,
            [CreateBundledTriangleCityObject("exact-root-bldg-one", worldPosition: bldgWorldPosition)]);

        Slot firstBldgRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(PrimarySourceFile)}");
        ResoniteFloat3 correctedRootPosition = new(14.0, 6.0, -9.0);
        firstBldgRoot.Position = CreateFloat3(correctedRootPosition);

        await ExecuteImportAsync(
            client,
            bldgMetadata,
            secondWorkDirectory.Path,
            [CreateBundledTriangleCityObject("exact-root-bldg-two", worldPosition: bldgWorldPosition)]);

        Slot secondBldgRoot = ResoniteLiveSceneImportTargetTestSupport.FindSlotsByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(PrimarySourceFile)}")
            .Single(slot => !string.Equals(slot.ID, firstBldgRoot.ID, StringComparison.Ordinal));

        AssertNear(correctedRootPosition, GetSlotPosition(secondBldgRoot), 0.2);
    }

    [Fact]
    public async Task ExecuteAsyncThirdAppendToleratesDuplicateExactSourceRootsWithSamePlacement()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path, [PrimarySourceFile]);
        using SceneSinkRecordingClient client = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();
        using TemporaryDirectory thirdWorkDirectory = new();
        ResoniteFloat3 worldPosition = new(10.0, 3.0, 20.0);

        await ExecuteImportAsync(
            client,
            metadata,
            firstWorkDirectory.Path,
            [CreateBundledTriangleCityObject("duplicate-exact-one", worldPosition: worldPosition)]);
        await ExecuteImportAsync(
            client,
            metadata,
            secondWorkDirectory.Path,
            [CreateBundledTriangleCityObject("duplicate-exact-two", worldPosition: worldPosition)]);
        await ExecuteImportAsync(
            client,
            metadata,
            thirdWorkDirectory.Path,
            [CreateBundledTriangleCityObject("duplicate-exact-three", worldPosition: worldPosition)]);

        Slot[] sourceRoots = ResoniteLiveSceneImportTargetTestSupport.FindSlotsByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(PrimarySourceFile)}");

        Assert.Equal(3, sourceRoots.Length);
        Assert.All(sourceRoots, sourceRoot => AssertNear(GetSlotPosition(sourceRoots[0]), GetSlotPosition(sourceRoot), 0.2));
    }

    [Fact]
    public async Task ExecuteAsyncAppendToleratesDuplicateParentAncestorRootsWithSamePlacement()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata demMetadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            ParentMeshCode,
            datasetDirectory.Path,
            RequireMeshCodeCenter(ParentMeshCode),
            packageNames: ["dem"],
            sourceFiles: [ParentDemSourceFile]);
        ImportedSceneMetadata bldgMetadata = CreateMetadata(datasetDirectory.Path, [PrimarySourceFile]);
        using SceneSinkRecordingClient client = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();
        using TemporaryDirectory thirdWorkDirectory = new();
        ResoniteFloat3 parentPosition = new(20.0, 8.0, 30.0);

        await ExecuteImportAsync(
            client,
            demMetadata,
            firstWorkDirectory.Path,
            [CreateTerrainGridDemCityObject("duplicate-parent-one", actualMeshCode: MeshCode, sourceFileRelativePath: ParentDemSourceFile, worldPosition: parentPosition)]);
        Slot firstParentRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(ParentDemSourceFile)}");
        await ExecuteImportAsync(
            client,
            demMetadata,
            secondWorkDirectory.Path,
            [CreateTerrainGridDemCityObject("duplicate-parent-two", actualMeshCode: MeshCode, sourceFileRelativePath: ParentDemSourceFile, worldPosition: parentPosition)]);
        Slot secondParentRoot = ResoniteLiveSceneImportTargetTestSupport.FindSlotsByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(ParentDemSourceFile)}")
            .Single(slot => !string.Equals(slot.ID, firstParentRoot.ID, StringComparison.Ordinal));
        secondParentRoot.Position = CreateFloat3(GetSlotPosition(firstParentRoot));

        await ExecuteImportAsync(
            client,
            bldgMetadata,
            thirdWorkDirectory.Path,
            [CreateBundledTriangleCityObject("duplicate-parent-bldg", worldPosition: new ResoniteFloat3(20.0, 8.0, 30.0))]);

        Slot bldgRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(PrimarySourceFile)}");
        ResoniteFloat3 expectedPosition = Add(GetSlotPosition(firstParentRoot), ComputeMeshCodeOffset(ParentMeshCode, MeshCode));
        AssertNear(expectedPosition, GetSlotPosition(bldgRoot), 0.2);
    }

    [Fact]
    public async Task ExecuteAsyncSiblingAppendUsesObservedRootWhenNoAncestorExists()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata firstRunMetadata = CreateMetadata(datasetDirectory.Path, [PrimarySourceFile, SecondarySourceFile]);
        ImportedSceneMetadata secondRunMetadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            ThirdMeshCode,
            datasetDirectory.Path,
            RequireMeshCodeCenter(ThirdMeshCode),
            packageNames: ["bldg"],
            sourceFiles: [ThirdSourceFile]);
        using SceneSinkRecordingClient client = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();

        await ExecuteImportAsync(
            client,
            firstRunMetadata,
            firstWorkDirectory.Path,
            [
                CreateBundledTriangleCityObject("sibling-base-primary", actualMeshCode: MeshCode, sourceFileRelativePath: PrimarySourceFile),
                CreateBundledTriangleCityObject("sibling-base-secondary", actualMeshCode: SecondaryMeshCode, sourceFileRelativePath: SecondarySourceFile),
            ]);
        await ExecuteImportAsync(
            client,
            secondRunMetadata,
            secondWorkDirectory.Path,
            [CreateBundledTriangleCityObject("sibling-append-third", actualMeshCode: ThirdMeshCode, sourceFileRelativePath: ThirdSourceFile)]);

        Slot primaryRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(PrimarySourceFile)}");
        Slot secondaryRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(SecondarySourceFile)}");
        Slot thirdRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(ThirdSourceFile)}");
        ResoniteFloat3 expectedFromPrimary = Add(GetSlotPosition(primaryRoot), ComputeMeshCodeOffset(MeshCode, ThirdMeshCode));
        ResoniteFloat3 expectedFromSecondary = Add(GetSlotPosition(secondaryRoot), ComputeMeshCodeOffset(SecondaryMeshCode, ThirdMeshCode));

        AssertNear(expectedFromPrimary, expectedFromSecondary, 0.25);
        AssertNear(expectedFromPrimary, GetSlotPosition(thirdRoot), 0.2);
    }

    [Fact]
    public async Task ExecuteAsyncSiblingAppendRejectsObservedRootsWithDifferentCoordinateFrames()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata firstRunMetadata = CreateMetadata(datasetDirectory.Path, [PrimarySourceFile, SecondarySourceFile]);
        ImportedSceneMetadata secondRunMetadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            ThirdMeshCode,
            datasetDirectory.Path,
            RequireMeshCodeCenter(ThirdMeshCode),
            packageNames: ["bldg"],
            sourceFiles: [ThirdSourceFile]);
        using SceneSinkRecordingClient client = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();

        await ExecuteImportAsync(
            client,
            firstRunMetadata,
            firstWorkDirectory.Path,
            [
                CreateBundledTriangleCityObject("sibling-ambiguous-primary", actualMeshCode: MeshCode, sourceFileRelativePath: PrimarySourceFile),
                CreateBundledTriangleCityObject("sibling-ambiguous-secondary", actualMeshCode: SecondaryMeshCode, sourceFileRelativePath: SecondarySourceFile),
            ]);
        Slot secondaryRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(SecondarySourceFile)}");
        ResoniteFloat3 shiftedSecondaryRoot = Add(GetSlotPosition(secondaryRoot), new ResoniteFloat3(1.0, 0.0, 0.0));
        secondaryRoot.Position = CreateFloat3(shiftedSecondaryRoot);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteImportAsync(
            client,
            secondRunMetadata,
            secondWorkDirectory.Path,
            [CreateBundledTriangleCityObject("sibling-ambiguous-third", actualMeshCode: ThirdMeshCode, sourceFileRelativePath: ThirdSourceFile)]));
        Assert.Contains("Append placement is ambiguous", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsyncReturnsLocationAnchoredToResolvedSourceFileRoot()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path, [PrimarySourceFile, SecondarySourceFile]);
        using SceneSinkRecordingClient client = new();
        using TemporaryDirectory workDirectory = new();
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client);

        SceneImportExecutionResult executionResult = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [
                CreateBundledTriangleCityObject(
                    "completion-root",
                    actualMeshCode: MeshCode,
                    sourceFileRelativePath: PrimarySourceFile,
                    worldPosition: new ResoniteFloat3(123.0, 0.0, 456.0)),
            ]);

        Slot sourceFileRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(PrimarySourceFile)}");
        string destination = Assert.Single(executionResult.Destinations);
        Assert.StartsWith("ws://localhost:12345/#", destination, StringComparison.Ordinal);
        string destinationAnchorId = GetDestinationAnchorId(destination);
        Assert.True(client.SlotsById.ContainsKey(destinationAnchorId));
        Assert.True(
            string.Equals(destinationAnchorId, sourceFileRoot.ID, StringComparison.Ordinal)
            || ResoniteLiveSceneImportTargetTestSupport.IsDescendantOf(client, sourceFileRoot.ID, destinationAnchorId));
    }

    [Fact]
    public async Task ExecuteAsyncAppendsIntoAssetsOnlyDatasetRootAndAnchorsFirstActualSourceFileRootAtDatasetRoot()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path, [PrimarySourceFile, SecondarySourceFile]);
        using SceneSinkRecordingClient client = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();

        await using (ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client))
        {
            _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(importTarget, metadata, firstWorkDirectory.Path, []);
        }

        await using (ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client))
        {
            SceneImportExecutionResult executionResult = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
                importTarget,
                metadata,
                secondWorkDirectory.Path,
                [
                    CreateBundledTriangleCityObject(
                        "assets-only-append",
                        actualMeshCode: SecondaryMeshCode,
                        sourceFileRelativePath: SecondarySourceFile,
                        worldPosition: new ResoniteFloat3(10.0, 0.0, 20.0)),
                ]);
            Slot sourceFileRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
                client,
                $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(SecondarySourceFile)}");
            ResoniteFloat3 expectedRootOffset = ComputeMeshCodeOffset(MeshCode, SecondaryMeshCode);
            Slot objectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject assets-only-append");

            Assert.Equal(expectedRootOffset.X, GetSlotPosition(sourceFileRoot).X, 3);
            Assert.Equal(expectedRootOffset.Z, GetSlotPosition(sourceFileRoot).Z, 3);
            AssertNear(new ResoniteFloat3(10.0, 0.0, 20.0), GetAccumulatedPosition(client, objectSlot), 0.2);
            string destination = Assert.Single(executionResult.Destinations);
            Assert.StartsWith("ws://localhost:12345/#", destination, StringComparison.Ordinal);
            string destinationAnchorId = GetDestinationAnchorId(destination);
            Assert.True(client.SlotsById.ContainsKey(destinationAnchorId));
            Assert.True(
                string.Equals(destinationAnchorId, sourceFileRoot.ID, StringComparison.Ordinal)
                || ResoniteLiveSceneImportTargetTestSupport.IsDescendantOf(client, sourceFileRoot.ID, destinationAnchorId));
        }
    }

    [Fact]
    public async Task ExecuteAsyncAppendWithDifferentSecondRunRequestMeshPreservesObjectLocalPosition()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata firstRunMetadata = CreateMetadata(datasetDirectory.Path, [PrimarySourceFile]);
        ImportedSceneMetadata secondRunMetadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            SecondaryMeshCode,
            datasetDirectory.Path,
            RequireMeshCodeCenter(SecondaryMeshCode),
            packageNames: ["bldg"],
            sourceFiles: [SecondarySourceFile]);
        using SceneSinkRecordingClient client = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();
        ResoniteFloat3 secondRunLocalPosition = new(10.0, 0.0, 20.0);

        await using (ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client))
        {
            _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
                importTarget,
                firstRunMetadata,
                firstWorkDirectory.Path,
                [
                    CreateBundledTriangleCityObject(
                        "base-run",
                        actualMeshCode: MeshCode,
                        sourceFileRelativePath: PrimarySourceFile,
                        worldPosition: new ResoniteFloat3(1.0, 0.0, 2.0)),
                ]);
        }

        await using (ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client))
        {
            _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
                importTarget,
                secondRunMetadata,
                secondWorkDirectory.Path,
                [
                    CreateBundledTriangleCityObject(
                        "append-run",
                        actualMeshCode: SecondaryMeshCode,
                        sourceFileRelativePath: SecondarySourceFile,
                        worldPosition: secondRunLocalPosition),
                ]);
        }

        Slot sourceFileRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(SecondarySourceFile)}");
        Slot objectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject append-run");
        ResoniteFloat3 expectedRootOffset = ComputeMeshCodeOffset(MeshCode, SecondaryMeshCode);
        ResoniteFloat3 expectedAccumulatedPosition = new(
            expectedRootOffset.X + secondRunLocalPosition.X,
            secondRunLocalPosition.Y,
            expectedRootOffset.Z + secondRunLocalPosition.Z);

        AssertNear(expectedRootOffset, GetSlotPosition(sourceFileRoot), 0.2);
        AssertNear(secondRunLocalPosition, GetSlotPosition(objectSlot), 0.2);
        AssertNear(expectedAccumulatedPosition, GetAccumulatedPosition(client, objectSlot), 0.2);
    }

    [Fact]
    public async Task ExecuteAsyncReusesSingleSourceFileRootAcrossConcurrentLodHierarchyCreation()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path, [SecondarySourceFile]);
        using SceneSinkRecordingClient client = new();
        using TemporaryDirectory workDirectory = new();
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client, enableMeshBake: false);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [
                CreateBundledTriangleCityObject(
                    "concurrent-lod1",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondarySourceFile,
                    worldPosition: new ResoniteFloat3(10.0, 0.0, 20.0),
                    lodLevel: 1),
                CreateBundledTriangleCityObject(
                    "concurrent-lod2",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondarySourceFile,
                    worldPosition: new ResoniteFloat3(11.0, 0.0, 21.0),
                    lodLevel: 2),
            ]);

        string sourceFileRootName = Path.GetFileNameWithoutExtension(SecondarySourceFile);
        Slot datasetRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, $"PLATEAU {DatasetName}");
        Slot assetsRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(client, $"PLATEAU {DatasetName}/Assets");

        Assert.Equal(
            1,
            client.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, sourceFileRootName, StringComparison.Ordinal)
                && string.Equals(slot.Parent?.TargetID, datasetRoot.ID, StringComparison.Ordinal)));
        Assert.Equal(
            1,
            client.SlotsById.Values.Count(slot => string.Equals(slot.Name?.Value, sourceFileRootName, StringComparison.Ordinal)
                && string.Equals(slot.Parent?.TargetID, assetsRoot.ID, StringComparison.Ordinal)));

        Slot datasetSourceFileRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{sourceFileRootName}");
        string[] lodChildren = client.SlotsById.Values
            .Where(slot => string.Equals(slot.Parent?.TargetID, datasetSourceFileRoot.ID, StringComparison.Ordinal))
            .Select(static slot => slot.Name?.Value ?? string.Empty)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["LOD1", "LOD2"], lodChildren);
    }

    private static ImportedSceneMetadata CreateMetadata(string datasetRoot, IReadOnlyList<string>? sourceFiles = null)
    {
        return ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetRoot,
            LocalOrigin,
            packageNames: ["bldg"],
            sourceFiles: sourceFiles ?? [PrimarySourceFile]);
    }

    private static ImportedSceneMetadata CreateDemMetadata(string datasetRoot, IReadOnlyList<string>? sourceFiles = null)
    {
        return ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetRoot,
            LocalOrigin,
            packageNames: ["dem"],
            sourceFiles: sourceFiles ?? [PrimaryDemSourceFile]);
    }

    private static async Task ExecuteImportAsync(
        SceneSinkRecordingClient client,
        ImportedSceneMetadata metadata,
        string workDirectory,
        IReadOnlyList<ResoniteConstructionCityObject> cityObjects)
    {
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client);
        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory,
            cityObjects);
    }

    private static ResoniteConstructionCityObject CreateBundledTriangleCityObject(
        string objectIdentity,
        ResoniteFloat2? textureScale = null,
        string family = BundledDefaultMaterialFamilies.WallResidentialPlasterLow,
        ResoniteMaterialProjection projection = ResoniteMaterialProjection.Uv,
        string actualMeshCode = MeshCode,
        string sourceFileRelativePath = PrimarySourceFile,
        ResoniteFloat3? worldPosition = null,
        int? lodLevel = 0)
    {
        DefaultCommonMaterialMember? commonMaterial = textureScale is null
            ? SelectTestBundledMember(family, 0)
            : null;
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: actualMeshCode,
            LodLevel: lodLevel,
            Transform: new ResoniteTransform(worldPosition ?? new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: projection,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    TextureScale: textureScale,
                    Family: family,
                    AssetScope: commonMaterial is null
                        ? ResoniteMaterialAssetScope.PresentationSlotScoped
                        : ResoniteMaterialAssetScope.Common,
                    BundledVariantIndex: 0,
                    CommonMaterial: commonMaterial),
            ],
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static ResoniteConstructionCityObject CreatePayloadTriangleCityObject(
        string objectIdentity,
        ResoniteTexturePayload payload,
        ResoniteFloat2? textureScale = null,
        ResoniteFloat2? textureOffset = null,
        string sourceFileRelativePath = PrimarySourceFile)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    TextureScale: textureScale,
                    Family: null,
                    TextureOffset: textureOffset,
                    AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped,
                    CommonMaterial: CommonMaterialCatalog.Create().Generic.Uv),
            ],
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static ResoniteConstructionCityObject CreateSameKeyPayloadOverrideCityObject(
        string objectIdentity,
        ResoniteTexturePayload firstPayload,
        ResoniteTexturePayload secondPayload,
        string sourceFileRelativePath = PrimarySourceFile)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: CreateQuadTwoSubmeshMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: firstPayload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    Family: null,
                    AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped,
                    CommonMaterial: CommonMaterialCatalog.Create().Generic.Uv),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: secondPayload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    Family: null,
                    AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped,
                    CommonMaterial: CommonMaterialCatalog.Create().Generic.Uv),
            ],
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static ResoniteConstructionCityObject CreateTerrainGridDemCityObject(
        string objectIdentity,
        string actualMeshCode = MeshCode,
        string sourceFileRelativePath = PrimaryDemSourceFile,
        ResoniteFloat3? worldPosition = null)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"DEM Terrain Grid {objectIdentity}",
            PackageName: "dem",
            ActualMeshCode: actualMeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(worldPosition ?? new ResoniteFloat3(0.0, 0.0, 0.0)),
            Geometry: new ResoniteTerrainGridGeometry(
                Width: 2,
                Height: 2,
                Size: new ResoniteFloat2(10.0, 10.0),
                MinHeight: 0.0,
                MaxHeight: 3.0,
                HeightSamples: [0.0, 1.0, 2.0, 3.0]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Wireframe,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static ResoniteConstructionCityObject CreateVertexColorTriangleCityObject(
        string objectIdentity,
        string sourceFileRelativePath = PrimarySourceFile)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.VertexColor,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    AssetScope: ResoniteMaterialAssetScope.Common,
                    CommonMaterial: CommonMaterialCatalog.Create().VertexColor.Uv),
            ],
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static ResoniteConstructionCityObject CreateMixedMaterialCityObject(
        string objectIdentity,
        ResoniteTexturePayload payload,
        string sourceFileRelativePath = PrimarySourceFile)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: CreateTwoSubmeshMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    Family: BundledDefaultMaterialFamilies.WallResidentialPlasterLow,
                    AssetScope: ResoniteMaterialAssetScope.Common,
                    BundledVariantIndex: 0,
                    CommonMaterial: CommonMaterialCatalog.Create().WallResidentialPlasterLow.ResidentialPlasterLow),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    Family: null,
                    AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped,
                    CommonMaterial: CommonMaterialCatalog.Create().Generic.Uv),
            ],
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static ResoniteImportedMesh CreateTwoSubmeshMesh()
    {
        return new ResoniteImportedMesh(
            Vertices:
            [
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(2.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(3.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(2.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
            ],
            Submeshes:
            [
                new ResoniteMeshSubmesh(0, [0, 1, 2]),
                new ResoniteMeshSubmesh(1, [3, 4, 5]),
            ]);
    }

    private static DefaultCommonMaterialMember SelectTestBundledMember(string family, int variantIndex)
    {
        CommonMaterialCatalog<DefaultCommonMaterialMember> catalog = CommonMaterialCatalog.Create();
        return family switch
        {
            BundledDefaultMaterialFamilies.Roof when variantIndex == 0 => catalog.Roof.Concrete012,
            BundledDefaultMaterialFamilies.WallResidentialPlasterLow when variantIndex == 0 => catalog.WallResidentialPlasterLow.ResidentialPlasterLow,
            _ => throw new InvalidOperationException($"Unexpected bundled material fixture '{family}' variant '{variantIndex}'."),
        };
    }

    private static ResoniteFloat3 GetSlotPosition(Slot slot)
    {
        return slot.Position is Field_float3 position
            ? new ResoniteFloat3(position.Value.x, position.Value.y, position.Value.z)
            : new ResoniteFloat3(0.0, 0.0, 0.0);
    }

    private static Field_float3 CreateFloat3(ResoniteFloat3 value)
    {
        return new Field_float3
        {
            Value = new float3
            {
                x = (float)value.X,
                y = (float)value.Y,
                z = (float)value.Z,
            },
        };
    }

    private static ResoniteFloat3 GetAccumulatedPosition(SceneSinkRecordingClient client, Slot slot)
    {
        ResoniteFloat3 accumulated = new(0.0, 0.0, 0.0);
        Slot? current = slot;
        while (current is not null)
        {
            accumulated = Add(accumulated, GetSlotPosition(current));
            if (current.Parent is null
                || string.IsNullOrWhiteSpace(current.Parent.TargetID)
                || !client.SlotsById.TryGetValue(current.Parent.TargetID, out current))
            {
                break;
            }
        }

        return accumulated;
    }

    private static ResoniteFloat3 Add(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    private static void AssertNear(ResoniteFloat3 expected, ResoniteFloat3 actual, double tolerance)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
        Assert.InRange(actual.Z, expected.Z - tolerance, expected.Z + tolerance);
    }

    private static ResoniteFloat3 ComputeMeshCodeOffset(string referenceMeshCode, string meshCode)
    {
        Assert.True(PlateauMeshCode.TryGetGeodeticCenter(referenceMeshCode, out GeodeticCoordinate referenceCenter));
        Assert.True(PlateauMeshCode.TryGetGeodeticCenter(meshCode, out GeodeticCoordinate currentCenter));
        LocalCartesian cartesian = new(
            referenceCenter.Latitude,
            referenceCenter.Longitude,
            referenceCenter.Altitude,
            Geocentric.WGS84);
        (double x, double y, double z) eun = cartesian.Forward(
            currentCenter.Latitude,
            currentCenter.Longitude,
            currentCenter.Altitude);
        return new ResoniteFloat3(eun.x, 0.0, eun.y);
    }

    private static ResoniteLocalOrigin RequireMeshCodeCenter(string meshCode)
    {
        Assert.True(PlateauMeshCode.TryGetGeodeticCenter(meshCode, out GeodeticCoordinate center));
        return new ResoniteLocalOrigin(center.Latitude, center.Longitude, center.Altitude);
    }

    private static string GetDestinationAnchorId(string destination)
    {
        int fragmentIndex = destination.IndexOf('#', StringComparison.Ordinal);
        Assert.True(fragmentIndex >= 0 && fragmentIndex < destination.Length - 1);
        return destination[(fragmentIndex + 1)..];
    }

    private static string GetRendererMaterialReferenceTarget(
        SceneSinkRecordingClient client,
        string slotName)
    {
        Component renderer = Assert.Single(
            client.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                    && string.Equals(client.SlotsById[request.ContainerSlotId].Name?.Value, slotName, StringComparison.Ordinal))
                .Select(static request => request.Data));
        SyncList materials = Assert.IsType<SyncList>(renderer.Members["Materials"]);
        Reference materialReference = Assert.IsType<Reference>(Assert.Single(materials.Elements));
        return materialReference.TargetID;
    }

    private static string[] GetRendererMaterialReferenceTargets(
        SceneSinkRecordingClient client,
        string slotName)
    {
        Component renderer = Assert.Single(
            client.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                    && string.Equals(client.SlotsById[request.ContainerSlotId].Name?.Value, slotName, StringComparison.Ordinal))
                .Select(static request => request.Data));
        SyncList materials = Assert.IsType<SyncList>(renderer.Members["Materials"]);
        return materials.Elements
            .Select(Assert.IsType<Reference>)
            .Select(static reference => reference.TargetID)
            .ToArray();
    }

    private static ResoniteImportedMesh CreateQuadTwoSubmeshMesh()
    {
        return new ResoniteImportedMesh(
            Vertices:
            [
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 1.0)),
            ],
            Submeshes:
            [
                new ResoniteMeshSubmesh(0, [0, 1, 2]),
                new ResoniteMeshSubmesh(1, [1, 3, 2]),
            ]);
    }

    private static string GetRendererMaterialPropertyBlockReferenceTarget(
        SceneSinkRecordingClient client,
        string slotName)
    {
        string? targetId = Assert.Single(GetRendererMaterialPropertyBlockReferenceTargets(client, slotName));
        return Assert.IsType<string>(targetId);
    }

    private static string?[] GetRendererMaterialPropertyBlockReferenceTargets(
        SceneSinkRecordingClient client,
        string slotName)
    {
        Component renderer = Assert.Single(
            client.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                    && string.Equals(client.SlotsById[request.ContainerSlotId].Name?.Value, slotName, StringComparison.Ordinal))
                .Select(static request => request.Data));
        SyncList propertyBlocks = Assert.IsType<SyncList>(renderer.Members["MaterialPropertyBlocks"]);
        return propertyBlocks.Elements
            .Select(Assert.IsType<Reference>)
            .Select(static reference => reference.TargetID)
            .ToArray();
    }

    private static int CountCommonMaterialComponents(SceneSinkRecordingClient client, string materialComponentId)
    {
        return client.AddedComponents.Count(request =>
            string.Equals(request.Data.ID, materialComponentId, StringComparison.Ordinal)
            && client.SlotPaths.TryGetValue(request.ContainerSlotId, out string? path)
            && path.Contains("PLATEAU Shared Assets/Common Materials/", StringComparison.Ordinal));
    }

    private static string CreateMeshUvSignature(
        ImportMeshRawData mesh)
    {
        Assert.Equal(3, mesh.VertexCount);
        return CreateMeshUvSignature(
            new ResoniteFloat2(mesh.AccessUV_2D(0)[0].x, mesh.AccessUV_2D(0)[0].y),
            new ResoniteFloat2(mesh.AccessUV_2D(0)[1].x, mesh.AccessUV_2D(0)[1].y),
            new ResoniteFloat2(mesh.AccessUV_2D(0)[2].x, mesh.AccessUV_2D(0)[2].y));
    }

    private static string CreateMeshUvSignature(
        ResoniteFloat2 firstUv,
        ResoniteFloat2 secondUv,
        ResoniteFloat2 thirdUv)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{firstUv.X:0.######},{firstUv.Y:0.######}|{secondUv.X:0.######},{secondUv.Y:0.######}|{thirdUv.X:0.######},{thirdUv.Y:0.######}");
    }

    private static bool IsSolidColorTexture(ResoniteRawTextureImport texture, byte r, byte g, byte b)
    {
        byte[] expectedPixel = [r, g, b, 255];
        return texture.Width == 2
            && texture.Height == 2
            && texture.RawRgba32Bytes.Chunk(4).All(pixel => pixel.SequenceEqual(expectedPixel));
    }

    private static async Task<string> SeedEmptyCurrentGenericSharedMaterialSlotAsync(SceneSinkRecordingClient client)
    {
        string sharedAssetsRootId = (await client.AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    Parent = new Reference { TargetID = "Root" },
                    Name = new Field_string { Value = "PLATEAU Shared Assets" },
                },
            },
            CancellationToken.None)).Slot.Value;
        string commonMaterialsRootId = (await client.AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    Parent = new Reference { TargetID = sharedAssetsRootId },
                    Name = new Field_string { Value = "Common Materials" },
                },
            },
            CancellationToken.None)).Slot.Value;
        string genericFamilySlotId = (await client.AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    Parent = new Reference { TargetID = commonMaterialsRootId },
                    Name = new Field_string { Value = "generic" },
                },
            },
            CancellationToken.None)).Slot.Value;

        return (await client.AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    Parent = new Reference { TargetID = genericFamilySlotId },
                    Name = new Field_string { Value = "uv" },
                },
            },
            CancellationToken.None)).Slot.Value;
    }

}
