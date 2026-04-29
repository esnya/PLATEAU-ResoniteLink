using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;
namespace PlateauResoniteLink.Tests.Targets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteMaterialPlanningTests
{
    [Fact]
    public async Task PlanCommonMaterialAssetAsyncImportsMetallicCompanionTextureWithLinearProfile()
    {
        using SceneSinkRecordingClient client = new();
        ResoniteMaterialPlanning planning = new(new BundledDefaultMaterialAssetStore());
        ResoniteMaterialBinding material = new(
            MaterialKey: "facade-common",
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
        Assert.NotNull(textureSet?.MetallicPath);

        PlannedDedicatedMaterialAsset plannedAsset = await planning.PlanCommonMaterialAssetAsync(
            client,
            material,
            CancellationToken.None);

        PlannedTextureAsset metallicAsset = Assert.Single(
            plannedAsset.Textures,
            texture => string.Equals(texture.Identity.Value, "metallic", StringComparison.Ordinal));
        int metallicImportIndex = int.Parse(
            metallicAsset.AssetUri.Segments[^1].TrimEnd('/'),
            CultureInfo.InvariantCulture);
        ResoniteRawTextureImport metallicTexture = client.ImportedRawTextures[metallicImportIndex];
        Assert.Equal(ResoniteTextureColorProfiles.Linear, metallicTexture.ColorProfile);
        Assert.Contains(
            plannedAsset.Textures,
            texture => string.Equals(texture.Identity.Value, "metallic", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveTerrainTextureCanvasMaterialComposesExistingTransformWithCanvasOccupancy()
    {
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 17,
            GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
            MaxTextureSize: 512);
        ResoniteMaterialBinding material = new(
            MaterialKey: "dem-overlay",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(0.4, 0.8),
            TextureOffset: new ResoniteFloat2(0.1, 0.2),
            TerrainOverlay: overlay);
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextures = new()
        {
            [overlay] = new GeneratedTerrainTexture(
                new ResoniteRawTextureImport(512, 256, ResoniteTextureColorProfiles.Srgb, new byte[512 * 256 * 4]),
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
    public void ResolveTerrainTextureCanvasMaterialIntroducesScaleOnlyWhenMaterialHadNoExplicitTransform()
    {
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 17,
            GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
            MaxTextureSize: 512);
        ResoniteMaterialBinding material = new(
            MaterialKey: "dem-overlay",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TerrainOverlay: overlay);
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextures = new()
        {
            [overlay] = new GeneratedTerrainTexture(
                new ResoniteRawTextureImport(512, 256, ResoniteTextureColorProfiles.Srgb, new byte[512 * 256 * 4]),
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
    public void CreateDedicatedMaterialIdentity_VariesByPackageIndexAndMaterialKey()
    {
        MaterialIdentity baseline = ResoniteMaterialPlanning.CreateDedicatedMaterialIdentity("bldg", 0, "material-a");
        MaterialIdentity repeatedBaseline = ResoniteMaterialPlanning.CreateDedicatedMaterialIdentity("bldg", 0, "material-a");
        MaterialIdentity differentPackage = ResoniteMaterialPlanning.CreateDedicatedMaterialIdentity("tran", 0, "material-a");
        MaterialIdentity differentIndex = ResoniteMaterialPlanning.CreateDedicatedMaterialIdentity("bldg", 1, "material-a");
        MaterialIdentity differentKey = ResoniteMaterialPlanning.CreateDedicatedMaterialIdentity("bldg", 0, "material-b");

        Assert.Equal(baseline, repeatedBaseline);
        Assert.NotEqual(baseline, differentPackage);
        Assert.NotEqual(baseline, differentIndex);
        Assert.NotEqual(baseline, differentKey);
    }

    [Fact]
    public async Task PlanMainTextureOverrideAsync_UsesPreparedUriWithRoleIdentity()
    {
        ResoniteMaterialBinding firstMaterial = new(
            MaterialKey: "material-a",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: new ResoniteTexturePayload(1, 1, "srgb", [255, 255, 255, 255], identity: "payload-a"),
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0]);
        ResoniteMaterialBinding secondMaterial = firstMaterial with
        {
            MaterialKey = "material-b",
        };
        Dictionary<ResoniteTexturePayload, Uri> firstPreparedUris = new(TexturePayloadReferenceComparer.Instance)
        {
            [firstMaterial.TexturePayload!] = new Uri("resdb:///texture/first", UriKind.Absolute),
        };
        Dictionary<ResoniteTexturePayload, Uri> secondPreparedUris = new(TexturePayloadReferenceComparer.Instance)
        {
            [secondMaterial.TexturePayload!] = new Uri("resdb:///texture/second", UriKind.Absolute),
        };

        PlannedTextureAsset? firstOverride = await ResoniteMaterialPlanning.PlanMainTextureOverrideAsync(
            firstMaterial,
            firstPreparedUris,
            new Dictionary<TerrainTextureOverlay, Uri>());
        PlannedTextureAsset? repeatedFirstOverride = await ResoniteMaterialPlanning.PlanMainTextureOverrideAsync(
            firstMaterial,
            firstPreparedUris,
            new Dictionary<TerrainTextureOverlay, Uri>());
        PlannedTextureAsset? secondOverride = await ResoniteMaterialPlanning.PlanMainTextureOverrideAsync(
            secondMaterial,
            secondPreparedUris,
            new Dictionary<TerrainTextureOverlay, Uri>());
        PlannedTextureAsset? thirdOverride = await ResoniteMaterialPlanning.PlanMainTextureOverrideAsync(
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

    private sealed class TexturePayloadReferenceComparer : IEqualityComparer<ResoniteTexturePayload>
    {
        internal static readonly TexturePayloadReferenceComparer Instance = new();

        public bool Equals(ResoniteTexturePayload? x, ResoniteTexturePayload? y) => ReferenceEquals(x, y);

        public int GetHashCode(ResoniteTexturePayload obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
