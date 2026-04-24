using System;
using System.Collections.Generic;
using System.IO;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

using ResoniteLink;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteMaterialComponentPolicyTests
{
    [Fact]
    public void CreateMembersBuildsUvStandardMaterialFields()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "facade",
            BaseColor: new ResoniteColor(0.1, 0.2, 0.3, 0.4),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: new ResoniteMaterialDepthOffset(2.0, 3.0),
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(0.5, 0.25),
            TextureOffset: new ResoniteFloat2(0.125, 0.75),
            Family: BundledDefaultMaterialFamilies.Facade,
            AssetScope: ResoniteMaterialAssetScope.Common,
            BundledVariantIndex: 0);

        string componentType = ResoniteMaterialComponentPolicy.GetComponentType(material);
        Dictionary<string, Member> members = ResoniteMaterialComponentPolicy.CreateMembers(material);

        Assert.Equal("[FrooxEngine]FrooxEngine.PBS_Metallic", componentType);
        Field_colorX albedo = Assert.IsType<Field_colorX>(members["AlbedoColor"]);
        Field_float2 textureScale = Assert.IsType<Field_float2>(members["TextureScale"]);
        Field_float2 textureOffset = Assert.IsType<Field_float2>(members["TextureOffset"]);
        Field_float offsetFactor = Assert.IsType<Field_float>(members["OffsetFactor"]);
        Field_float offsetUnits = Assert.IsType<Field_float>(members["OffsetUnits"]);

        Assert.Equal(0.1f, albedo.Value.r, 6);
        Assert.Equal(0.2f, albedo.Value.g, 6);
        Assert.Equal(0.3f, albedo.Value.b, 6);
        Assert.Equal(0.4f, albedo.Value.a, 6);
        Assert.Equal(ResoniteColorSpace.SrgbProfile, albedo.Value.Profile);
        Assert.Equal(0.5f, textureScale.Value.x, 6);
        Assert.Equal(0.25f, textureScale.Value.y, 6);
        Assert.Equal(0.125f, textureOffset.Value.x, 6);
        Assert.Equal(0.75f, textureOffset.Value.y, 6);
        Assert.Equal(2.0f, offsetFactor.Value, 6);
        Assert.Equal(3.0f, offsetUnits.Value, 6);
    }

    [Fact]
    public void CreateMembersUsesSrgbProfileForVertexColorMaterialFields()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "vertex-color",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.VertexColor,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            AssetScope: ResoniteMaterialAssetScope.Common);

        Dictionary<string, Member> members = ResoniteMaterialComponentPolicy.CreateMembers(material);

        Field_colorX albedo = Assert.IsType<Field_colorX>(members["AlbedoColor"]);
        Assert.Equal(ResoniteColorSpace.SrgbProfile, albedo.Value.Profile);
    }

    [Fact]
    public void CreateMembersRejectsRawNonCommonUvTransformBeforeBake()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "dynamic-overlay",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: new ResoniteTexturePayload(1, 1, "srgb", [255, 255, 255, 255], "textures/dynamic.png"),
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(0.5, 0.25),
            TextureOffset: new ResoniteFloat2(0.125, 0.75),
            AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => ResoniteMaterialComponentPolicy.CreateMembers(material));
        Assert.Contains("Bake city-object UV transforms into mesh UVs before emission.", error.Message, StringComparison.Ordinal);
        Assert.Contains("projection=Uv", error.Message, StringComparison.Ordinal);
        Assert.Contains("texture=texture-payload", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateMembersBuildsTriplanarAndWireframeFields()
    {
        ResoniteMaterialBinding triplanarMaterial = new(
            MaterialKey: "road",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Triplanar,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(0.25, 0.125),
            Family: BundledDefaultMaterialFamilies.Road,
            BundledVariantIndex: 0);
        ResoniteMaterialBinding wireframeMaterial = new(
            MaterialKey: "overlay",
            BaseColor: new ResoniteColor(0.2, 0.4, 0.6, 0.5),
            MaterialType: ResoniteMaterialType.Wireframe,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0]);

        Dictionary<string, Member> triplanarMembers = ResoniteMaterialComponentPolicy.CreateMembers(triplanarMaterial);
        Dictionary<string, Member> wireframeMembers = ResoniteMaterialComponentPolicy.CreateMembers(wireframeMaterial);

        Assert.Equal("[FrooxEngine]FrooxEngine.PBS_TriplanarMetallic", ResoniteMaterialComponentPolicy.GetComponentType(triplanarMaterial));
        Field_float2 triplanarTextureScale = Assert.IsType<Field_float2>(triplanarMembers["TextureScale"]);
        Field_float2 triplanarTextureOffset = Assert.IsType<Field_float2>(triplanarMembers["TextureOffset"]);
        Assert.IsType<Field_float>(triplanarMembers["Metallic"]);
        Assert.IsType<Field_float>(triplanarMembers["TriplanarBlendPower"]);
        Assert.IsType<Field_bool>(triplanarMembers["ObjectSpace"]);
        Assert.Equal(0.25f, triplanarTextureScale.Value.x, 6);
        Assert.Equal(0.125f, triplanarTextureScale.Value.y, 6);
        Assert.Equal(0.0f, triplanarTextureOffset.Value.x, 6);
        Assert.Equal(0.0f, triplanarTextureOffset.Value.y, 6);

        Assert.Equal("[FrooxEngine]FrooxEngine.WireframeMaterial", ResoniteMaterialComponentPolicy.GetComponentType(wireframeMaterial));
        Field_float thickness = Assert.IsType<Field_float>(wireframeMembers["Thickness"]);
        Field_colorX fillColor = Assert.IsType<Field_colorX>(wireframeMembers["FillColor"]);
        Assert.Equal(0.01f, thickness.Value, 6);
        Assert.Equal(0.04f, fillColor.Value.a, 6);
    }

    [Fact]
    public void CreateMembersCreatesUvMaterialWithoutTransformAfterNormalization()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "direct-heightmap-style-material",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: new ResoniteTexturePayload(1, 1, "srgb", [255, 255, 255, 255], "textures/direct-heightmap-style.png"),
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(1.0, 1.0),
            TextureOffset: new ResoniteFloat2(0.125, 0.75),
            AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);

        ResoniteMaterialBinding normalized = ResoniteDynamicMaterialUvNormalizer.NormalizeMaterialBinding(material);

        Dictionary<string, Member> members = ResoniteMaterialComponentPolicy.CreateMembers(normalized);

        Assert.DoesNotContain("TextureScale", members.Keys);
        Assert.DoesNotContain("TextureOffset", members.Keys);
    }

    [Fact]
    public void CreateMembersRejectsOffsetOnlyUvTransformBeforeBake()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "offset-only-material",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureOffset: new ResoniteFloat2(0.125, 0.75),
            TerrainOverlay: new TerrainTextureOverlay(
                PackageName: "dem",
                UrlTemplate: "https://example.invalid/{z}/{x}/{y}.png",
                ZoomLevel: 17,
                GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
                MaxTextureSize: 512),
            AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => ResoniteMaterialComponentPolicy.CreateMembers(material));
        Assert.Contains("Bake city-object UV transforms into mesh UVs before emission.", error.Message, StringComparison.Ordinal);
        Assert.Contains("texture=terrain-overlay", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateMembersPreservesUvTransformForSharedTerrainOverlayMaterial()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "shared-heightmap-overlay",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(0.5, 0.25),
            TextureOffset: new ResoniteFloat2(0.125, 0.75),
            TerrainOverlay: new TerrainTextureOverlay(
                PackageName: "dem",
                UrlTemplate: "https://example.invalid/{z}/{x}/{y}.png",
                ZoomLevel: 17,
                GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
                MaxTextureSize: 512),
            AssetScope: ResoniteMaterialAssetScope.Common);

        Dictionary<string, Member> members = ResoniteMaterialComponentPolicy.CreateMembers(material);

        Field_float2 textureScale = Assert.IsType<Field_float2>(members["TextureScale"]);
        Field_float2 textureOffset = Assert.IsType<Field_float2>(members["TextureOffset"]);
        Assert.Equal(0.5f, textureScale.Value.x, 6);
        Assert.Equal(0.25f, textureScale.Value.y, 6);
        Assert.Equal(0.125f, textureOffset.Value.x, 6);
        Assert.Equal(0.75f, textureOffset.Value.y, 6);
    }

    [Fact]
    public void TryGetBundledCompanionTextureSetResolvesSiblingTextures()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "facade",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            Family: BundledDefaultMaterialFamilies.Facade,
            BundledVariantIndex: 0);

        bool resolved = ResoniteMaterialComponentPolicy.TryGetBundledCompanionTextureSet(
            new BundledDefaultMaterialAssetStore(),
            material,
            out BundledDefaultMaterialTextureSet? textureSet);

        Assert.True(resolved);
        Assert.NotNull(textureSet);
        Assert.True(textureSet.EmissionPath is null || textureSet.EmissionPath.EndsWith("_Emission.jpg", StringComparison.Ordinal));
        Assert.EndsWith("_Height.jpg", textureSet.HeightPath, StringComparison.Ordinal);
        Assert.EndsWith("_Metallic.png", textureSet.MetallicPath, StringComparison.Ordinal);
        Assert.EndsWith("_NormalGL.jpg", textureSet.NormalPath, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetBundledCompanionTextureSetResolvesCityFurnitureCompanions()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "city-furniture",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Triplanar,
            DepthOffset: null,
            SubmeshIndices: [0],
            Family: BundledDefaultMaterialFamilies.CityFurniture,
            BundledVariantIndex: 0);

        bool resolved = ResoniteMaterialComponentPolicy.TryGetBundledCompanionTextureSet(
            new BundledDefaultMaterialAssetStore(),
            material,
            out BundledDefaultMaterialTextureSet? textureSet);

        Assert.True(resolved);
        Assert.NotNull(textureSet);
        Assert.Null(textureSet.EmissionPath);
        Assert.EndsWith("Plaster002_2K-JPG_Height.jpg", textureSet.HeightPath, StringComparison.Ordinal);
        Assert.EndsWith("Plaster002_2K-JPG_Metallic.png", textureSet.MetallicPath, StringComparison.Ordinal);
        Assert.EndsWith("Plaster002_2K-JPG_NormalGL.jpg", textureSet.NormalPath, StringComparison.Ordinal);
    }

    [Fact]
    public void BundledDefaultMaterialAssetStoreResolvesCityFurnitureAsset()
    {
        string logicalPath = BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.CityFurniture, 0);

        bool resolved = new BundledDefaultMaterialAssetStore().TryGetAbsolutePath(logicalPath, out string absolutePath);

        Assert.True(resolved);
        Assert.EndsWith("Plaster002_2K-JPG_Color.jpg", absolutePath, StringComparison.Ordinal);
        Assert.True(File.Exists(absolutePath));
    }

    [Fact]
    public void BundledDefaultCityFurnitureAlbedoStaysNearNeutralWhite()
    {
        string logicalPath = BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.CityFurniture, 0);
        Assert.True(new BundledDefaultMaterialAssetStore().TryGetAbsolutePath(logicalPath, out string absolutePath));

        using Image<Rgba32> image = Image.Load<Rgba32>(absolutePath);
        double totalR = 0.0;
        double totalG = 0.0;
        double totalB = 0.0;
        int sampleCount = 0;

        for (int y = 0; y < image.Height; y += 32)
        {
            for (int x = 0; x < image.Width; x += 32)
            {
                Rgba32 pixel = image[x, y];
                totalR += pixel.R;
                totalG += pixel.G;
                totalB += pixel.B;
                sampleCount++;
            }
        }

        double averageR = totalR / sampleCount;
        double averageG = totalG / sampleCount;
        double averageB = totalB / sampleCount;

        Assert.InRange(averageR, 210.0, 255.0);
        Assert.InRange(averageG, 210.0, 255.0);
        Assert.InRange(averageB, 210.0, 255.0);
        Assert.True(averageR - averageB <= 3.5, $"Expected city-furniture albedo to stay near neutral white, but sampled RGB was {averageR:F1}/{averageG:F1}/{averageB:F1}.");
        Assert.True(averageG - averageB <= 3.5, $"Expected city-furniture albedo to stay near neutral white, but sampled RGB was {averageR:F1}/{averageG:F1}/{averageB:F1}.");
    }
}
