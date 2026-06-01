using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

using static PlateauResoniteLink.Tests.TextureImportSourceTestFactory;

namespace PlateauResoniteLink.Tests.Targets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteMaterialPlanningTests
{
    [Fact]
    public void BundledTextureImportKeySeparatesSameTextureAssetByColorProfile()
    {
        BundledTextureImportKey srgbKey = new(
            BundledDefaultTextureAssets.Facade.Facade001.Albedo,
            ResoniteTextureColorProfiles.Srgb);
        BundledTextureImportKey linearKey = new(
            BundledDefaultTextureAssets.Facade.Facade001.Albedo,
            ResoniteTextureColorProfiles.Linear);

        Assert.NotEqual(srgbKey, linearKey);
    }

    [Fact]
    public async Task PlanCommonMaterialAssetAsyncImportsMetallicCompanionTextureWithLinearProfile()
    {
        using SceneSinkRecordingClient client = new();
        ResoniteMaterialPlanning planning = new(new BundledDefaultMaterialAssetStore());
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            Family: BundledDefaultMaterialFamilies.Facade,
            AssetScope: ResoniteMaterialAssetScope.Common,
            BundledVariantIndex: 0);
        _ = ResoniteMaterialComponentPolicy.TryGetBundledCompanionTextureSet(
            new BundledDefaultMaterialAssetStore(),
            material,
            out BundledDefaultMaterialTextureSet? textureSet);
        Assert.NotNull(textureSet?.Metallic);

        PlannedDedicatedMaterialAsset plannedAsset = await planning.PlanCommonMaterialAssetAsync(
            client,
            material,
            new AsyncInFlightResultCache<BundledTextureImportKey, Uri>(),
            CancellationToken.None);

        PlannedTextureAsset metallicAsset = Assert.Single(
            plannedAsset.Textures,
            texture => string.Equals(texture.Identity.Value, "metallic", StringComparison.Ordinal));
        int metallicImportIndex = int.Parse(
            metallicAsset.AssetUri.Segments[^1].TrimEnd('/'),
            CultureInfo.InvariantCulture);
        RawTexturePayload metallicTexture = ImportedRgba32Textures(client)[metallicImportIndex];
        Assert.Equal(ResoniteTextureColorProfiles.Linear, metallicTexture.ColorProfile);
        Assert.Contains(
            plannedAsset.Textures,
            texture => string.Equals(texture.Identity.Value, "metallic", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlanCommonMaterialAssetAsyncReusesBundledTextureImportsAcrossProjectionVariants()
    {
        using SceneSinkRecordingClient client = new();
        ResoniteMaterialPlanning planning = new(new BundledDefaultMaterialAssetStore());
        AsyncInFlightResultCache<BundledTextureImportKey, Uri> bundledTextureImportTasks = new();
        ResoniteMaterialBinding uvMaterial = CreateRoadMaterial(ResoniteMaterialProjection.Uv);
        ResoniteMaterialBinding triplanarMaterial = CreateRoadMaterial(ResoniteMaterialProjection.Triplanar);

        _ = await planning.PlanCommonMaterialAssetAsync(
            client,
            uvMaterial,
            bundledTextureImportTasks,
            CancellationToken.None);
        int importCountAfterUv = ImportedRgba32Textures(client).Count;

        _ = await planning.PlanCommonMaterialAssetAsync(
            client,
            triplanarMaterial,
            bundledTextureImportTasks,
            CancellationToken.None);

        Assert.Equal(importCountAfterUv, ImportedRgba32Textures(client).Count);
    }

    [Fact]
    public async Task PlanCommonMaterialAssetAsyncReusesSharedMaterialMapImportsAcrossColorVariants()
    {
        using SceneSinkRecordingClient client = new();
        ResoniteMaterialPlanning planning = new(new BundledDefaultMaterialAssetStore());
        AsyncInFlightResultCache<BundledTextureImportKey, Uri> bundledTextureImportTasks = new();
        ResoniteMaterialBinding baseMaterial = CreateBundledMaterial(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, 0);
        ResoniteMaterialBinding colorVariantMaterial = CreateBundledMaterial(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, 1);

        PlannedDedicatedMaterialAsset basePlannedAsset = await planning.PlanCommonMaterialAssetAsync(
            client,
            baseMaterial,
            bundledTextureImportTasks,
            CancellationToken.None);
        int importCountAfterBase = ImportedRgba32Textures(client).Count;

        PlannedDedicatedMaterialAsset colorVariantPlannedAsset = await planning.PlanCommonMaterialAssetAsync(
            client,
            colorVariantMaterial,
            bundledTextureImportTasks,
            CancellationToken.None);

        Assert.Equal(importCountAfterBase + 3, ImportedRgba32Textures(client).Count);
        Assert.NotEqual(
            GetTextureUri(basePlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Albedo),
            GetTextureUri(colorVariantPlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Albedo));
        Assert.NotEqual(
            GetTextureUri(basePlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Normal),
            GetTextureUri(colorVariantPlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Normal));
        Assert.NotEqual(
            GetTextureUri(basePlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Height),
            GetTextureUri(colorVariantPlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Height));
        Assert.Equal(
            GetTextureUri(basePlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Metallic),
            GetTextureUri(colorVariantPlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Metallic));
        Assert.Equal(
            GetTextureUri(basePlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Emission),
            GetTextureUri(colorVariantPlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Emission));
    }

    [Fact]
    public async Task PlanCommonMaterialAssetAsyncReusesOnlyIdenticalMaterialMapImports()
    {
        using SceneSinkRecordingClient client = new();
        ResoniteMaterialPlanning planning = new(new BundledDefaultMaterialAssetStore());
        AsyncInFlightResultCache<BundledTextureImportKey, Uri> bundledTextureImportTasks = new();
        ResoniteMaterialBinding baseMaterial = CreateBundledMaterial(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 0);
        ResoniteMaterialBinding colorVariantMaterial = CreateBundledMaterial(BundledDefaultMaterialFamilies.FacadeHighriseNightLow, 0);

        PlannedDedicatedMaterialAsset basePlannedAsset = await planning.PlanCommonMaterialAssetAsync(
            client,
            baseMaterial,
            bundledTextureImportTasks,
            CancellationToken.None);
        int importCountAfterBase = ImportedRgba32Textures(client).Count;

        PlannedDedicatedMaterialAsset colorVariantPlannedAsset = await planning.PlanCommonMaterialAssetAsync(
            client,
            colorVariantMaterial,
            bundledTextureImportTasks,
            CancellationToken.None);

        Assert.Equal(importCountAfterBase + 2, ImportedRgba32Textures(client).Count);
        Assert.NotEqual(
            GetTextureUri(basePlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Albedo),
            GetTextureUri(colorVariantPlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Albedo));
        Assert.Equal(
            GetTextureUri(basePlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Normal),
            GetTextureUri(colorVariantPlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Normal));
        Assert.Equal(
            GetTextureUri(basePlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Height),
            GetTextureUri(colorVariantPlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Height));
        Assert.Equal(
            GetTextureUri(basePlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Metallic),
            GetTextureUri(colorVariantPlannedAsset, ResoniteSceneMaterialConventions.TextureMemberRole.Metallic));
        Assert.Null(ResoniteMaterialPlanning.TryGetPlannedTextureUri(
            basePlannedAsset.Textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Emission));
        Assert.NotNull(ResoniteMaterialPlanning.TryGetPlannedTextureUri(
            colorVariantPlannedAsset.Textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Emission));
    }

    [Fact]
    public async Task PlanDedicatedMaterialAssetAsyncUsesPreserveDedicatedMaterialSlotContract()
    {
        using SceneSinkRecordingClient client = new();
        ResoniteMaterialPlanning planning = new(new BundledDefaultMaterialAssetStore());
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0]);

        PlannedDedicatedMaterialAsset preserved = await planning.PlanDedicatedMaterialAssetAsync(
            client,
            material,
            materialIndex: 2,
            new Dictionary<ResoniteTexturePayload, Uri>(),
            new Dictionary<TerrainTextureOverlay, Uri>(),
            preserveDedicatedMaterialSlot: true,
            CancellationToken.None);
        PlannedDedicatedMaterialAsset notPreserved = await planning.PlanDedicatedMaterialAssetAsync(
            client,
            material,
            materialIndex: 2,
            new Dictionary<ResoniteTexturePayload, Uri>(),
            new Dictionary<TerrainTextureOverlay, Uri>(),
            preserveDedicatedMaterialSlot: false,
            CancellationToken.None);
        string expectedDedicatedSlotName = ResoniteSceneMaterialConventions.CreateDedicatedMaterialSlotName(
            material,
            materialIndex: 2);

        Assert.True(preserved.PreserveDedicatedMaterialSlot);
        Assert.Equal(expectedDedicatedSlotName, preserved.DedicatedMaterialSlotName);
        Assert.False(notPreserved.PreserveDedicatedMaterialSlot);
        Assert.Null(notPreserved.DedicatedMaterialSlotName);
    }

    [Fact]
    public void ResolveTerrainTextureCanvasMaterialComposesExistingTransformWithCanvasOccupancy()
    {
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 17,
            GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
            MaxTextureSize: 512);
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(0.4, 0.8),
            TextureOffset: new ResoniteFloat2(0.1, 0.2),
            TerrainOverlayMaterial: new TerrainOverlayMaterialBinding(overlay));
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextures = new()
        {
            [overlay] = new GeneratedTerrainTexture(
                CreateRawTextureSource(512, 256, ResoniteTextureColorProfiles.Srgb, new byte[512 * 256 * 4]),
                new ResoniteFloat2(0.5, 0.25),
                new ResoniteFloat2(0.0, 0.0)),
        };

        ResoniteMaterialBinding effectiveMaterial = ResoniteMaterialPlanning.ResolveTerrainTextureCanvasMaterial(
            material,
            preparedTerrainTextures);

        Assert.Equal(new ResoniteFloat2(0.2, 0.2), effectiveMaterial.TextureScale);
        Assert.Equal(new ResoniteFloat2(0.05, 0.05), effectiveMaterial.TextureOffset);
    }

    [Fact]
    public void AddCommonMaterialComponentsDoesNotCreateEmissionMembersWithoutEmissionSource()
    {
        ResoniteBatchOperations.BatchActionBuilder batchBuilder = new();
        ResoniteMaterialBinding material = CreateBundledMaterial(BundledDefaultMaterialFamilies.Facade, 0);
        PlannedDedicatedMaterialAsset plannedMaterial = new(
            material,
            [
                new PlannedTextureAsset(
                    ResoniteSceneMaterialConventions.CreateTextureIdentity(ResoniteSceneMaterialConventions.TextureMemberRole.Albedo),
                    new Uri("resdb:///texture/albedo", UriKind.Absolute)),
            ],
            PreserveDedicatedMaterialSlot: false);

        ResoniteMaterialPlanning.AddCommonMaterialComponents(batchBuilder, plannedMaterial, "slot");

        AddComponent materialComponent = Assert.Single(
            batchBuilder.Actions.OfType<AddComponent>(),
            action => action.Data.ComponentType == "[FrooxEngine]FrooxEngine.PBS_Metallic");
        Assert.DoesNotContain("EmissiveMap", materialComponent.Data.Members.Keys);
        Assert.DoesNotContain("EmissiveColor", materialComponent.Data.Members.Keys);
    }

    [Fact]
    public void AddCommonMaterialComponentsCreatesEmissionMembersWithEmissionSource()
    {
        ResoniteBatchOperations.BatchActionBuilder batchBuilder = new();
        ResoniteMaterialBinding material = CreateBundledMaterial(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, 0);
        PlannedDedicatedMaterialAsset plannedMaterial = new(
            material,
            [
                new PlannedTextureAsset(
                    ResoniteSceneMaterialConventions.CreateTextureIdentity(ResoniteSceneMaterialConventions.TextureMemberRole.Albedo),
                    new Uri("resdb:///texture/albedo", UriKind.Absolute)),
                new PlannedTextureAsset(
                    ResoniteSceneMaterialConventions.CreateTextureIdentity(ResoniteSceneMaterialConventions.TextureMemberRole.Emission),
                    new Uri("resdb:///texture/emission", UriKind.Absolute)),
            ],
            PreserveDedicatedMaterialSlot: false);

        ResoniteMaterialPlanning.AddCommonMaterialComponents(batchBuilder, plannedMaterial, "slot");

        AddComponent materialComponent = Assert.Single(
            batchBuilder.Actions.OfType<AddComponent>(),
            action => action.Data.ComponentType == "[FrooxEngine]FrooxEngine.PBS_Metallic");
        Assert.IsType<Reference>(materialComponent.Data.Members["EmissiveMap"]);
        Field_colorX emissiveColor = Assert.IsType<Field_colorX>(materialComponent.Data.Members["EmissiveColor"]);
        Assert.Equal(1.0f, emissiveColor.Value.r, 6);
        Assert.Equal(1.0f, emissiveColor.Value.g, 6);
        Assert.Equal(1.0f, emissiveColor.Value.b, 6);
    }

    [Fact]
    public void ResolveTerrainTextureCanvasMaterialIntroducesScaleOnlyWhenMaterialHadNoExplicitTransform()
    {
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 17,
            GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
            MaxTextureSize: 512);
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TerrainOverlayMaterial: new TerrainOverlayMaterialBinding(overlay));
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextures = new()
        {
            [overlay] = new GeneratedTerrainTexture(
                CreateRawTextureSource(512, 256, ResoniteTextureColorProfiles.Srgb, new byte[512 * 256 * 4]),
                new ResoniteFloat2(0.5, 0.25),
                new ResoniteFloat2(0.0, 0.0)),
        };

        ResoniteMaterialBinding effectiveMaterial = ResoniteMaterialPlanning.ResolveTerrainTextureCanvasMaterial(
            material,
            preparedTerrainTextures);

        Assert.Equal(new ResoniteFloat2(0.5, 0.25), effectiveMaterial.TextureScale);
        Assert.Null(effectiveMaterial.TextureOffset);
    }

    [Fact]
    public void PlanMainTextureOverrideUsesPreparedUriWithRoleIdentity()
    {
        ResoniteMaterialBinding firstMaterial = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: new ResoniteTexturePayload(1, 1, "srgb", [255, 255, 255, 255], identity: "payload-a"),
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0]);
        ResoniteMaterialBinding secondMaterial = firstMaterial with
        {
        };
        Dictionary<ResoniteTexturePayload, Uri> firstPreparedUris = new(TexturePayloadReferenceComparer.Instance)
        {
            [firstMaterial.TexturePayload!] = new Uri("resdb:///texture/first", UriKind.Absolute),
        };
        Dictionary<ResoniteTexturePayload, Uri> secondPreparedUris = new(TexturePayloadReferenceComparer.Instance)
        {
            [secondMaterial.TexturePayload!] = new Uri("resdb:///texture/second", UriKind.Absolute),
        };

        PlannedTextureAsset? firstOverride = ResoniteMaterialPlanning.PlanMainTextureOverride(
            firstMaterial,
            firstPreparedUris,
            new Dictionary<TerrainTextureOverlay, Uri>());
        PlannedTextureAsset? repeatedFirstOverride = ResoniteMaterialPlanning.PlanMainTextureOverride(
            firstMaterial,
            firstPreparedUris,
            new Dictionary<TerrainTextureOverlay, Uri>());
        PlannedTextureAsset? secondOverride = ResoniteMaterialPlanning.PlanMainTextureOverride(
            secondMaterial,
            secondPreparedUris,
            new Dictionary<TerrainTextureOverlay, Uri>());
        PlannedTextureAsset? thirdOverride = ResoniteMaterialPlanning.PlanMainTextureOverride(
            firstMaterial,
            firstPreparedUris,
            new Dictionary<TerrainTextureOverlay, Uri>());

        Assert.NotNull(firstOverride);
        Assert.NotNull(repeatedFirstOverride);
        Assert.NotNull(secondOverride);
        Assert.NotNull(thirdOverride);
        Assert.Equal(new TextureIdentity("main"), firstOverride!.Identity);
        Assert.Equal(firstOverride.Identity, repeatedFirstOverride!.Identity);
        Assert.Equal(firstOverride.Identity, secondOverride!.Identity);
        Assert.Equal(firstOverride.Identity, thirdOverride!.Identity);
        Assert.Equal(new Uri("resdb:///texture/first", UriKind.Absolute), firstOverride.AssetUri);
        Assert.Equal(firstOverride.AssetUri, repeatedFirstOverride.AssetUri);
        Assert.Equal(new Uri("resdb:///texture/second", UriKind.Absolute), secondOverride.AssetUri);
    }

    private static ResoniteMaterialBinding CreateRoadMaterial(ResoniteMaterialProjection projection)
    {
        return new ResoniteMaterialBinding(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: projection,
            DepthOffset: null,
            SubmeshIndices: [0],
            Family: projection == ResoniteMaterialProjection.Uv
                ? BundledDefaultMaterialFamilies.RoadUv
                : BundledDefaultMaterialFamilies.RoadTriplanar,
            AssetScope: ResoniteMaterialAssetScope.Common,
            BundledVariantIndex: 0);
    }

    private static ResoniteMaterialBinding CreateBundledMaterial(string family, int variantIndex)
    {
        return new ResoniteMaterialBinding(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            Family: family,
            AssetScope: ResoniteMaterialAssetScope.Common,
            BundledVariantIndex: variantIndex);
    }

    private static Uri GetTextureUri(
        PlannedDedicatedMaterialAsset plannedAsset,
        ResoniteSceneMaterialConventions.TextureMemberRole role)
    {
        return ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedAsset.Textures, role)
            ?? throw new InvalidOperationException($"Missing planned texture role '{role}'.");
    }

    private sealed class TexturePayloadReferenceComparer : IEqualityComparer<ResoniteTexturePayload>
    {
        internal static readonly TexturePayloadReferenceComparer Instance = new();

        public bool Equals(ResoniteTexturePayload? x, ResoniteTexturePayload? y) => ReferenceEquals(x, y);

        public int GetHashCode(ResoniteTexturePayload obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
