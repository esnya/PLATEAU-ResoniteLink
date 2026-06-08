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

using ResoniteLink;

using static PlateauResoniteLink.Tests.TextureImportSourceTestFactory;

namespace PlateauResoniteLink.Tests.Targets;

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

        Assert.Equal(firstMaterialId, secondMaterialId);
        AssertResolvedComponent(client, firstMaterialId);
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

        Assert.Equal(firstMaterialId, secondMaterialId);
        AssertResolvedComponent(client, firstMaterialId);
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
        string firstPropertyBlockId = GetRendererMaterialPropertyBlockReferenceTarget(client, "CityObject dataset-texture-one");
        string secondPropertyBlockId = GetRendererMaterialPropertyBlockReferenceTarget(client, "CityObject dataset-texture-two");

        Assert.Equal(firstMaterialId, secondMaterialId);
        AssertResolvedComponent(client, firstMaterialId);
        Assert.NotEqual(firstPropertyBlockId, secondPropertyBlockId);
        Assert.Contains(ImportedRgba32Textures(client), static texture => IsSolidColorTexture(texture, 255, 0, 0));
        Assert.Contains(ImportedRgba32Textures(client), static texture => IsSolidColorTexture(texture, 0, 255, 0));
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
        AssertResolvedComponent(client, firstMaterialId);
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

        Assert.Equal(firstMaterialId, secondMaterialId);
        AssertResolvedComponent(client, firstMaterialId);
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

        Assert.Equal(firstMaterialId, secondMaterialId);
        AssertResolvedComponent(client, firstMaterialId);
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
        AssertResolvedComponent(client, firstMaterialId);
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
        AddComponent materialComponentRequest = Assert.Single(
            client.AddedComponents,
            request => string.Equals(request.Data.ID, rendererMaterialId, StringComparison.Ordinal));

        Assert.Equal(emptyCurrentMaterialSlotId, materialComponentRequest.ContainerSlotId);
        AssertResolvedComponent(client, rendererMaterialId);
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
    public async Task ExecuteAsyncReadsExistingSharedMaterialAssets()
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
        AssertResolvedComponent(client, rendererMaterialId);
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
        AssertResolvedComponent(client, firstMaterialId);
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
        string?[] propertyBlockIds = GetRendererMaterialPropertyBlockReferenceTargets(client, "CityObject mixed-material-order");

        AssertResolvedComponent(client, materialIds[0]);
        AssertResolvedComponent(client, materialIds[1]);
        Assert.Null(propertyBlockIds[0]);
        Assert.NotNull(propertyBlockIds[1]);
        Assert.Contains(ImportedRgba32Textures(client), static texture => IsSolidColorTexture(texture, 255, 0, 0));
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

        string firstMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject reuse-run-one");
        string secondMaterialId = GetRendererMaterialReferenceTarget(client, "CityObject reuse-run-two");

        Assert.Equal(firstMaterialId, secondMaterialId);
        AssertResolvedComponent(client, firstMaterialId);
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
        AssertResolvedComponent(client, firstMaterialId);
    }

    [Fact]
    public async Task ExecuteAsyncPreservesWorldPositionForNonCompletionMesh()
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

        Slot objectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject offset-run-one");
        ResoniteFloat3 accumulatedPosition = GetAccumulatedPosition(client, objectSlot);
        Assert.Equal(worldPosition.X, accumulatedPosition.X, 3);
        Assert.Equal(worldPosition.Y, accumulatedPosition.Y, 3);
        Assert.Equal(worldPosition.Z, accumulatedPosition.Z, 3);
    }

    [Fact]
    public async Task ExecuteAsyncPositionsInitialSourceFileRootsRelativeToSelectedDataCenter()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteLocalOrigin selectedDataCenter = RequireMergedMeshCodeCenter([MeshCode, SecondaryMeshCode]);
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            selectedDataCenter,
            packageNames: ["bldg"],
            sourceFiles: [PrimarySourceFile, SecondarySourceFile],
            requestedMeshCodes: [MeshCode, SecondaryMeshCode]);
        using SceneSinkRecordingClient client = new();
        ResoniteFloat3 primaryWorldPosition = new(12.0, 0.0, 34.0);
        ResoniteFloat3 secondaryWorldPosition = new(56.0, 0.0, 78.0);

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [
                CreateBundledTriangleCityObject(
                    "selected-center-primary",
                    actualMeshCode: MeshCode,
                    sourceFileRelativePath: PrimarySourceFile,
                    worldPosition: primaryWorldPosition),
                CreateBundledTriangleCityObject(
                    "selected-center-secondary",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondarySourceFile,
                    worldPosition: secondaryWorldPosition),
            ],
            client);

        Slot primaryRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(PrimarySourceFile)}");
        Slot secondaryRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
            client,
            $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(SecondarySourceFile)}");
        Slot primaryObjectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject selected-center-primary");
        Slot secondaryObjectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject selected-center-secondary");

        AssertNear(ComputeOriginOffset(selectedDataCenter, MeshCode), GetSlotPosition(primaryRoot), 0.2);
        AssertNear(ComputeOriginOffset(selectedDataCenter, SecondaryMeshCode), GetSlotPosition(secondaryRoot), 0.2);
        AssertNear(primaryWorldPosition, GetAccumulatedPosition(client, primaryObjectSlot), 0.2);
        AssertNear(secondaryWorldPosition, GetAccumulatedPosition(client, secondaryObjectSlot), 0.2);
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
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client, enableMeshBake: false);
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

        Assert.Equal(12.5, GetSlotPosition(sourceFileRoot).Y, 3);

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

        Slot firstObjectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject lod0-first");
        Slot secondObjectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject lod0-second");

        AssertNear(new ResoniteFloat3(10.0, 0.0, 20.0), GetAccumulatedPosition(client, firstObjectSlot), 0.2);
        AssertNear(new ResoniteFloat3(11.0, 0.0, 21.0), GetAccumulatedPosition(client, secondObjectSlot), 0.2);
    }

    [Fact]
    public async Task ExecuteAsyncPreservesWorldPositionForTerrainGridDem()
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

        Slot objectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "DEM Terrain Grid dem-terrain-grid-run-one");
        ResoniteFloat3 accumulatedPosition = GetAccumulatedPosition(client, objectSlot);
        Assert.Equal(worldPosition.X, accumulatedPosition.X, 3);
        Assert.Equal(worldPosition.Y, accumulatedPosition.Y, 3);
        Assert.Equal(worldPosition.Z, accumulatedPosition.Z, 3);
    }

    [Fact]
    public async Task ExecuteAsyncAlignsThirdMeshBuildingAndParentMeshDemSourceRootsInWorldSpace()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            DatasetName,
            MeshCode,
            datasetDirectory.Path,
            LocalOrigin,
            packageNames: ["bldg", "dem"],
            sourceFiles: [SecondarySourceFile, ParentDemSourceFile]);
        using SceneSinkRecordingClient client = new();
        ResoniteFloat3 sharedThirdMeshWorldPosition = ComputeOriginOffset(LocalOrigin, SecondaryMeshCode);

        await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            metadata,
            [
                CreateBundledTriangleCityObject(
                    "bldg-third-mesh-root",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: SecondarySourceFile,
                    sourceFileRootMeshCode: SecondaryMeshCode,
                    worldPosition: sharedThirdMeshWorldPosition),
                CreateTerrainGridDemCityObject(
                    "dem-parent-mesh-root",
                    actualMeshCode: SecondaryMeshCode,
                    sourceFileRelativePath: ParentDemSourceFile,
                    sourceFileRootMeshCode: ParentMeshCode,
                    worldPosition: sharedThirdMeshWorldPosition),
            ],
            client);

        Slot buildingSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(
            client,
            "CityObject bldg-third-mesh-root");
        Slot demSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(
            client,
            "DEM Terrain Grid dem-parent-mesh-root");
        ResoniteFloat3 buildingWorldPosition = GetAccumulatedPosition(client, buildingSlot);
        ResoniteFloat3 demWorldPosition = GetAccumulatedPosition(client, demSlot);

        AssertNear(sharedThirdMeshWorldPosition, buildingWorldPosition, 0.2);
        AssertNear(sharedThirdMeshWorldPosition, demWorldPosition, 0.2);
        AssertNear(buildingWorldPosition, demWorldPosition, 0.001);
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

        Slot objectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "DEM Terrain Grid dem-terrain-grid-run-two");
        ResoniteFloat3 accumulatedPosition = GetAccumulatedPosition(client, objectSlot);
        AssertNear(secondRunWorldPosition, accumulatedPosition, 0.2);
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

        Slot firstObjectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject duplicate-exact-one");
        Slot secondObjectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject duplicate-exact-two");
        Slot thirdObjectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject duplicate-exact-three");

        AssertNear(worldPosition, GetAccumulatedPosition(client, firstObjectSlot), 0.2);
        AssertNear(worldPosition, GetAccumulatedPosition(client, secondObjectSlot), 0.2);
        AssertNear(worldPosition, GetAccumulatedPosition(client, thirdObjectSlot), 0.2);
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
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client, enableMeshBake: false);

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
    public async Task ExecuteAsyncAppendsIntoAssetsOnlyDatasetRootUsingDatasetRootLocalAnchor()
    {
        using TemporaryDirectory datasetDirectory = new();
        ImportedSceneMetadata metadata = CreateMetadata(datasetDirectory.Path, [PrimarySourceFile, SecondarySourceFile]);
        using SceneSinkRecordingClient client = new();
        using TemporaryDirectory firstWorkDirectory = new();
        using TemporaryDirectory secondWorkDirectory = new();
        ResoniteFloat3 datasetRootPosition = new(25.0, 6.0, -14.0);
        ResoniteFloat3 primaryObjectPosition = new(4.0, 1.0, 8.0);
        ResoniteFloat3 secondaryObjectPosition = new(10.0, 2.0, 20.0);

        await using (ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client, enableMeshBake: false))
        {
            _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(importTarget, metadata, firstWorkDirectory.Path, []);
        }

        Slot datasetRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(client, $"PLATEAU {DatasetName}");
        datasetRoot.Position = CreateFloat3(datasetRootPosition);

        await using (ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client, enableMeshBake: false))
        {
            SceneImportExecutionResult executionResult = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
                importTarget,
                metadata,
                secondWorkDirectory.Path,
                [
                    CreateBundledTriangleCityObject(
                        "assets-only-primary",
                        actualMeshCode: MeshCode,
                        sourceFileRelativePath: PrimarySourceFile,
                        worldPosition: primaryObjectPosition),
                    CreateBundledTriangleCityObject(
                        "assets-only-secondary",
                        actualMeshCode: SecondaryMeshCode,
                        sourceFileRelativePath: SecondarySourceFile,
                        worldPosition: secondaryObjectPosition),
                ]);
            Slot primarySourceFileRoot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByPathSuffix(
                client,
                $"PLATEAU {DatasetName}/{Path.GetFileNameWithoutExtension(PrimarySourceFile)}");
            Slot primaryObjectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject assets-only-primary");
            Slot secondaryObjectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject assets-only-secondary");

            AssertNear(new ResoniteFloat3(0.0, 0.0, 0.0), GetSlotPosition(primarySourceFileRoot), 0.001);
            AssertNear(Add(datasetRootPosition, primaryObjectPosition), GetAccumulatedPosition(client, primaryObjectSlot), 0.2);
            AssertNear(Add(datasetRootPosition, secondaryObjectPosition), GetAccumulatedPosition(client, secondaryObjectSlot), 0.2);
            string destination = Assert.Single(executionResult.Destinations);
            Assert.StartsWith("ws://localhost:12345/#", destination, StringComparison.Ordinal);
            string destinationAnchorId = GetDestinationAnchorId(destination);
            Assert.True(client.SlotsById.ContainsKey(destinationAnchorId));
            Assert.True(
                string.Equals(destinationAnchorId, primarySourceFileRoot.ID, StringComparison.Ordinal)
                || ResoniteLiveSceneImportTargetTestSupport.IsDescendantOf(client, primarySourceFileRoot.ID, destinationAnchorId));
        }
    }

    [Fact]
    public async Task ExecuteAsyncAppendWithDifferentSecondRunRequestMeshPreservesAccumulatedPosition()
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

        await using (ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client, enableMeshBake: false))
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

        await using (ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client, enableMeshBake: false))
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

        Slot objectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject append-run");
        ResoniteFloat3 expectedRootOffset = ComputeMeshCodeOffset(MeshCode, SecondaryMeshCode);
        ResoniteFloat3 expectedAccumulatedPosition = new(
            expectedRootOffset.X + secondRunLocalPosition.X,
            secondRunLocalPosition.Y,
            expectedRootOffset.Z + secondRunLocalPosition.Z);

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

        Slot firstObjectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject concurrent-lod1");
        Slot secondObjectSlot = ResoniteLiveSceneImportTargetTestSupport.FindUniqueSlotByNameOutsideAssets(client, "CityObject concurrent-lod2");

        Assert.Single(
            client.SlotsById.Values,
            slot => string.Equals(slot.Name?.Value, Path.GetFileNameWithoutExtension(SecondarySourceFile), StringComparison.Ordinal)
                && ResoniteLiveSceneImportTargetTestSupport.IsDescendantOf(client, firstObjectSlot.ID, slot.ID)
                && ResoniteLiveSceneImportTargetTestSupport.IsDescendantOf(client, secondObjectSlot.ID, slot.ID));
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
        await using ResoniteLiveSceneImportTarget importTarget = ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(client, enableMeshBake: false);
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
        string? sourceFileRootMeshCode = null,
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
                    BundledVariantIndex: 0,
                    AssetBinding: commonMaterial is null
                        ? ResoniteMaterialAssetBinding.Presentation
                        : ResoniteMaterialAssetBinding.SharedCommon(commonMaterial)),
            ],
            SourceFileRelativePath: sourceFileRelativePath,
            SourceFileRootMeshCode: sourceFileRootMeshCode);
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
                    AssetBinding: ResoniteMaterialAssetBinding.PresentationCommon(CommonMaterialCatalog.Create().Generic.Uv)),
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
                    AssetBinding: ResoniteMaterialAssetBinding.PresentationCommon(CommonMaterialCatalog.Create().Generic.Uv)),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: secondPayload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    Family: null,
                    AssetBinding: ResoniteMaterialAssetBinding.PresentationCommon(CommonMaterialCatalog.Create().Generic.Uv)),
            ],
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static ResoniteConstructionCityObject CreateTerrainGridDemCityObject(
        string objectIdentity,
        string actualMeshCode = MeshCode,
        string sourceFileRelativePath = PrimaryDemSourceFile,
        string? sourceFileRootMeshCode = null,
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
                    SubmeshIndices: [0],
                    ResoniteMaterialAssetBinding.Presentation),
            ],
            SourceFileRelativePath: sourceFileRelativePath,
            SourceFileRootMeshCode: sourceFileRootMeshCode);
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
                    AssetBinding: ResoniteMaterialAssetBinding.SharedCommon(CommonMaterialCatalog.Create().VertexColor.Uv)),
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
                    BundledVariantIndex: 0,
                    AssetBinding: ResoniteMaterialAssetBinding.SharedCommon(CommonMaterialCatalog.Create().WallResidentialPlasterLow.ResidentialPlasterLow)),
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: payload,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    Family: null,
                    AssetBinding: ResoniteMaterialAssetBinding.PresentationCommon(CommonMaterialCatalog.Create().Generic.Uv)),
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

    private static ResoniteFloat3 ComputeOriginOffset(ResoniteLocalOrigin referenceCenter, string meshCode)
    {
        return ResonitePlacementPolicy.ComputeOriginOffset(referenceCenter, RequireMeshCodeCenter(meshCode));
    }

    private static ResoniteLocalOrigin RequireMergedMeshCodeCenter(IReadOnlyList<string> meshCodes)
    {
        List<(double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude)> bounds = [];
        foreach (string meshCode in meshCodes)
        {
            Assert.True(PlateauMeshCode.TryGetBounds(meshCode, out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) meshBounds));
            bounds.Add(meshBounds);
        }

        return new ResoniteLocalOrigin(
            (bounds.Min(static bound => bound.SouthLatitude) + bounds.Max(static bound => bound.NorthLatitude)) / 2.0,
            (bounds.Min(static bound => bound.WestLongitude) + bounds.Max(static bound => bound.EastLongitude)) / 2.0,
            0.0);
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

    private static void AssertResolvedComponent(SceneSinkRecordingClient client, string componentId)
    {
        Assert.True(client.ComponentsById.ContainsKey(componentId));
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

    private static bool IsSolidColorTexture(RawTexturePayload texture, byte r, byte g, byte b)
    {
        byte[] expectedPixel = [r, g, b, 255];
        return texture.Width == 2
            && texture.Height == 2
            && texture.Bytes.Chunk(4).All(pixel => pixel.SequenceEqual(expectedPixel));
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
