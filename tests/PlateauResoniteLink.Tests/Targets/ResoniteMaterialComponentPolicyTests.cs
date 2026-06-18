using PlateauResoniteLink.Application.Importing.Contracts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

using ResoniteLink;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteMaterialComponentPolicyTests
{
    [Fact]
    public void CreateMembersEmitsNonDefaultCommonBundledFamilyUvTransformMembers()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(0.1, 0.2, 0.3, 0.4),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: new ResoniteMaterialDepthOffset(2.0, 3.0),
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(0.5, 0.25),
            TextureOffset: new ResoniteFloat2(0.125, 0.75),
            Family: BundledDefaultMaterialFamilies.FacadeHighriseGlass,
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedBundled(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 0),
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
    public void CreateMembersOmitsOnlyComponentDefaultUvTransformMembers()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(1.0, 1.0),
            TextureOffset: new ResoniteFloat2(0.0, 0.0),
            Family: BundledDefaultMaterialFamilies.FacadeHighriseGlass,
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedBundled(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 0),
            BundledVariantIndex: 0);

        Dictionary<string, Member> members = ResoniteMaterialComponentPolicy.CreateMembers(material);

        Assert.DoesNotContain("TextureScale", members.Keys);
        Assert.DoesNotContain("TextureOffset", members.Keys);
    }

    [Fact]
    public void CreateMembersUsesSrgbProfileForVertexColorMaterialFields()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.VertexColor,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedGenericUv());

        Dictionary<string, Member> members = ResoniteMaterialComponentPolicy.CreateMembers(material);

        Field_colorX albedo = Assert.IsType<Field_colorX>(members["AlbedoColor"]);
        Assert.Equal(ResoniteColorSpace.SrgbProfile, albedo.Value.Profile);
    }

    [Fact]
    public void CreateMembersRejectsRawNonCommonUvTransformBeforeNormalization()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: new RawRgba32ResoniteTexturePayload(1, 1, "srgb", [255, 255, 255, 255], "textures/dynamic.png"),
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            AssetBinding: ResoniteMaterialAssetBinding.Presentation,
            TextureScale: new ResoniteFloat2(0.5, 0.25),
            TextureOffset: new ResoniteFloat2(0.125, 0.75));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => ResoniteMaterialComponentPolicy.CreateMembers(material));
        Assert.Contains("Normalize city-object UV transforms into mesh UVs before emission.", error.Message, StringComparison.Ordinal);
        Assert.Contains("projection=Uv", error.Message, StringComparison.Ordinal);
        Assert.Contains("texture=texture-payload", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateMembersBuildsTriplanarAndWireframeFields()
    {
        ResoniteMaterialBinding triplanarMaterial = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Triplanar,
            DepthOffset: null,
            SubmeshIndices: [0],
            AssetBinding: ResoniteMaterialAssetBinding.Presentation,
            TextureScale: new ResoniteFloat2(0.25, 0.125),
            Family: BundledDefaultMaterialFamilies.RoadTriplanar,
            BundledVariantIndex: 0);
        ResoniteMaterialBinding wireframeMaterial = new(
            BaseColor: new ResoniteColor(0.2, 0.4, 0.6, 0.5),
            MaterialType: ResoniteMaterialType.Wireframe,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            AssetBinding: ResoniteMaterialAssetBinding.Presentation);

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
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: new RawRgba32ResoniteTexturePayload(1, 1, "srgb", [255, 255, 255, 255], "textures/direct-terrain-grid-style.png"),
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            AssetBinding: ResoniteMaterialAssetBinding.Presentation,
            TextureScale: new ResoniteFloat2(1.0, 1.0),
            TextureOffset: new ResoniteFloat2(0.125, 0.75));

        ResoniteMaterialBinding normalized = ResoniteDynamicMaterialUvNormalizer.NormalizeMaterialBinding(material);

        Dictionary<string, Member> members = ResoniteMaterialComponentPolicy.CreateMembers(normalized);

        Assert.DoesNotContain("TextureScale", members.Keys);
        Assert.DoesNotContain("TextureOffset", members.Keys);
    }

    [Fact]
    public void CreateMembersRejectsOffsetOnlyUvTransformBeforeNormalization()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            AssetBinding: ResoniteMaterialAssetBinding.Presentation,
            TextureOffset: new ResoniteFloat2(0.125, 0.75),
            TerrainOverlayMaterial: new TerrainOverlayMaterialBinding(
                ThirdRegionalMeshCode.Parse("53394525"),
                new TerrainTextureOverlay(
                    PackageName: "dem",
                    MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
                    UrlTemplate: "https://example.invalid/{z}/{x}/{y}.png",
                    ZoomLevel: 17,
                    GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
                    MaxTextureSize: 512)));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => ResoniteMaterialComponentPolicy.CreateMembers(material));
        Assert.Contains("Normalize city-object UV transforms into mesh UVs before emission.", error.Message, StringComparison.Ordinal);
        Assert.Contains("texture=terrain-overlay", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetBundledCompanionTextureSetResolvesSiblingTextures()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            AssetBinding: ResoniteMaterialAssetBinding.Presentation,
            Family: BundledDefaultMaterialFamilies.FacadeHighriseGlass,
            BundledVariantIndex: 0);

        bool resolved = ResoniteMaterialComponentPolicy.TryGetBundledCompanionTextureSet(
            new BundledDefaultMaterialAssetStore(),
            material,
            out BundledDefaultMaterialTextureSet? textureSet);

        Assert.True(resolved);
        Assert.NotNull(textureSet);
        string? emissionPath = GetPath(textureSet.Emission);
        Assert.True(emissionPath is null || emissionPath.EndsWith("_Emission.jpg", StringComparison.Ordinal));
        Assert.EndsWith("_Height.jpg", GetPath(textureSet.Height), StringComparison.Ordinal);
        Assert.EndsWith("_Metallic.png", GetPath(textureSet.Metallic), StringComparison.Ordinal);
        Assert.EndsWith("_NormalGL.jpg", GetPath(textureSet.Normal), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingBundledTextureDiagnosticIncludesMaterialAndTextureContext()
    {
        BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> missingAsset = new(
            "default-materials/ambientcg/facade/Missing_NormalGL.jpg");
        MethodInfo method = typeof(ResoniteMaterialComponentPolicy)
            .GetMethod("EnsureBundledTextureExists", BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(BundledDefaultNormalTextureRole));

        TargetInvocationException error = Assert.Throws<TargetInvocationException>(
            () => method.Invoke(
                null,
                [
                    new BundledDefaultMaterialAssetStore(),
                    missingAsset,
                    BundledDefaultMaterialFamilies.Facade,
                    2,
                ]));
        InvalidOperationException inner = Assert.IsType<InvalidOperationException>(error.InnerException);

        Assert.Contains("default-materials/ambientcg/facade/Missing_NormalGL.jpg", inner.Message, StringComparison.Ordinal);
        Assert.Contains("family 'facade'", inner.Message, StringComparison.Ordinal);
        Assert.Contains("variant 2", inner.Message, StringComparison.Ordinal);
        Assert.Contains("role 'BundledDefaultNormalTextureRole'", inner.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BundledFacadeCompanionTextureSetsResolveRequiredMaps()
    {
        IReadOnlyList<string> variants = BundledDefaultMaterialFamilies.GetVariants(BundledDefaultMaterialFamilies.Facade);
        for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
        {
            ResoniteMaterialBinding material = new(
                BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                MaterialType: ResoniteMaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                Projection: ResoniteMaterialProjection.Uv,
                DepthOffset: null,
                SubmeshIndices: [0],
                AssetBinding: ResoniteMaterialAssetBinding.Presentation,
                Family: BundledDefaultMaterialFamilies.FacadeHighriseGlass,
                BundledVariantIndex: variantIndex);

            bool resolved = ResoniteMaterialComponentPolicy.TryGetBundledCompanionTextureSet(
                new BundledDefaultMaterialAssetStore(),
                material,
                out BundledDefaultMaterialTextureSet? textureSet);

            Assert.True(resolved);
            Assert.NotNull(textureSet);
            BundledDefaultMaterialAssetStore assetStore = new();
            Assert.True(
                textureSet.Height is not null && assetStore.Contains(textureSet.Height),
                $"Missing height companion for facade variant '{variants[variantIndex]}'.");
            Assert.True(
                textureSet.Metallic is not null && assetStore.Contains(textureSet.Metallic),
                $"Missing packed metallic companion for facade variant '{variants[variantIndex]}'.");
            Assert.True(
                textureSet.Normal is not null && assetStore.Contains(textureSet.Normal),
                $"Missing normal companion for facade variant '{variants[variantIndex]}'.");
        }
    }

    [Fact]
    public void TryGetBundledCompanionTextureSetResolvesCityFurnitureCompanions()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Triplanar,
            DepthOffset: null,
            SubmeshIndices: [0],
            AssetBinding: ResoniteMaterialAssetBinding.Presentation,
            Family: BundledDefaultMaterialFamilies.CityFurniture,
            BundledVariantIndex: 0);

        bool resolved = ResoniteMaterialComponentPolicy.TryGetBundledCompanionTextureSet(
            new BundledDefaultMaterialAssetStore(),
            material,
            out BundledDefaultMaterialTextureSet? textureSet);

        Assert.True(resolved);
        Assert.NotNull(textureSet);
        Assert.Null(GetPath(textureSet.Emission));
        Assert.EndsWith("Plaster002_2K-JPG_Height.jpg", GetPath(textureSet.Height), StringComparison.Ordinal);
        Assert.EndsWith("Plaster002_2K-JPG_Metallic.png", GetPath(textureSet.Metallic), StringComparison.Ordinal);
        Assert.EndsWith("Plaster002_2K-JPG_NormalGL.jpg", GetPath(textureSet.Normal), StringComparison.Ordinal);
    }

    [Fact]
    public void BundledDefaultMaterialAssetStoreResolvesCityFurnitureAsset()
    {
        string logicalPath = BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.CityFurniture, 0);
        BundledDefaultMaterialAssetStore assetStore = new();

        bool resolved = assetStore.Contains(logicalPath);

        Assert.True(resolved);
        using Stream stream = assetStore.OpenRead(logicalPath);
        using Image image = Image.Load(stream);
        Assert.True(image.Width > 0);
        Assert.True(image.Height > 0);
    }

    [Fact]
    public void BundledGeneratedFacadeCompanionTextureSetUsesResonitePackageNaming()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            Family: BundledDefaultMaterialFamilies.WallResidentialPlasterLow,
            BundledVariantIndex: 0,
            TextureScale: new ResoniteFloat2(1.0 / 3.0, 1.0 / 3.0),
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedBundled(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, 0));

        bool resolved = ResoniteMaterialComponentPolicy.TryGetBundledCompanionTextureSet(
            new BundledDefaultMaterialAssetStore(),
            material,
            out BundledDefaultMaterialTextureSet? textureSet);

        Assert.True(resolved);
        Assert.NotNull(textureSet);
        string normalizedEmissionPath = GetPath(textureSet.Emission)!.Replace('\\', '/');
        string normalizedHeightPath = GetPath(textureSet.Height)!.Replace('\\', '/');
        string normalizedMetallicPath = GetPath(textureSet.Metallic)!.Replace('\\', '/');
        string normalizedNormalPath = GetPath(textureSet.Normal)!.Replace('\\', '/');
        Assert.Contains("default-materials/wallskins/wall_res_plaster_low/", normalizedEmissionPath, StringComparison.Ordinal);
        Assert.Contains("default-materials/wallskins/wall_res_plaster_low/", normalizedHeightPath, StringComparison.Ordinal);
        Assert.Contains("default-materials/wallskins/wall_res_plaster_low/", normalizedMetallicPath, StringComparison.Ordinal);
        Assert.Contains("default-materials/wallskins/wall_res_plaster_low/", normalizedNormalPath, StringComparison.Ordinal);
        Assert.EndsWith("emission.png", GetPath(textureSet.Emission), StringComparison.Ordinal);
        Assert.EndsWith("height.png", GetPath(textureSet.Height), StringComparison.Ordinal);
        Assert.EndsWith("metallic_ao_smoothness.png", GetPath(textureSet.Metallic), StringComparison.Ordinal);
        Assert.EndsWith("normalGL.png", GetPath(textureSet.Normal), StringComparison.Ordinal);
    }

    [Fact]
    public void BundledGeneratedFacadeColorVariantCompanionTextureSetSharesOnlyEmissionTexture()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            Family: BundledDefaultMaterialFamilies.WallResidentialPlasterLow,
            BundledVariantIndex: 1,
            TextureScale: new ResoniteFloat2(1.0 / 3.0, 1.0 / 3.0),
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedBundled(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, 1));

        bool resolved = ResoniteMaterialComponentPolicy.TryGetBundledCompanionTextureSet(
            new BundledDefaultMaterialAssetStore(),
            material,
            out BundledDefaultMaterialTextureSet? textureSet);

        Assert.True(resolved);
        Assert.NotNull(textureSet);
        string normalizedEmissionPath = GetPath(textureSet.Emission)!.Replace('\\', '/');
        string normalizedHeightPath = GetPath(textureSet.Height)!.Replace('\\', '/');
        string normalizedMetallicPath = GetPath(textureSet.Metallic)!.Replace('\\', '/');
        string normalizedNormalPath = GetPath(textureSet.Normal)!.Replace('\\', '/');
        Assert.Contains("default-materials/wallskins/wall_res_plaster_low/", normalizedEmissionPath, StringComparison.Ordinal);
        Assert.Contains("default-materials/wallskins/wall_res_plaster_dark/", normalizedHeightPath, StringComparison.Ordinal);
        Assert.Contains("default-materials/wallskins/wall_res_plaster_low/", normalizedMetallicPath, StringComparison.Ordinal);
        Assert.Contains("default-materials/wallskins/wall_res_plaster_dark/", normalizedNormalPath, StringComparison.Ordinal);
    }

    [Fact]
    public void BundledFacadeHighriseNightCompanionTextureSetSharesOnlyIdenticalMaterialMaps()
    {
        ResoniteMaterialBinding material = new(
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            Family: BundledDefaultMaterialFamilies.FacadeHighriseNightLow,
            BundledVariantIndex: 0,
            AssetBinding: ResoniteMaterialAssetBindingTestFactory.SharedBundled(BundledDefaultMaterialFamilies.FacadeHighriseNightLow, 0));

        bool resolved = ResoniteMaterialComponentPolicy.TryGetBundledCompanionTextureSet(
            new BundledDefaultMaterialAssetStore(),
            material,
            out BundledDefaultMaterialTextureSet? textureSet);

        Assert.True(resolved);
        Assert.NotNull(textureSet);
        string normalizedEmissionPath = GetPath(textureSet.Emission)!.Replace('\\', '/');
        string normalizedHeightPath = GetPath(textureSet.Height)!.Replace('\\', '/');
        string normalizedMetallicPath = GetPath(textureSet.Metallic)!.Replace('\\', '/');
        string normalizedNormalPath = GetPath(textureSet.Normal)!.Replace('\\', '/');
        Assert.Contains("default-materials/ambientcg/facade/Facade002_2K-JPG_Emission.jpg", normalizedEmissionPath, StringComparison.Ordinal);
        Assert.Contains("default-materials/ambientcg/facade/Facade001_2K-JPG_Height.jpg", normalizedHeightPath, StringComparison.Ordinal);
        Assert.Contains("default-materials/ambientcg/facade/Facade001_2K-JPG_Metallic.png", normalizedMetallicPath, StringComparison.Ordinal);
        Assert.Contains("default-materials/ambientcg/facade/Facade001_2K-JPG_NormalGL.jpg", normalizedNormalPath, StringComparison.Ordinal);
    }

    [Fact]
    public void BundledDefaultCityFurnitureAlbedoStaysNearNeutralWhite()
    {
        string logicalPath = BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.CityFurniture, 0);
        BundledDefaultMaterialAssetStore assetStore = new();
        Assert.True(assetStore.Contains(logicalPath));

        using Stream stream = assetStore.OpenRead(logicalPath);
        using Image<Rgba32> image = Image.Load<Rgba32>(stream);
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
        Assert.True(averageR - averageB <= 8.0, $"Expected city-furniture albedo to stay near light neutral plaster, but sampled RGB was {averageR:F1}/{averageG:F1}/{averageB:F1}.");
        Assert.True(averageG - averageB <= 8.0, $"Expected city-furniture albedo to stay near light neutral plaster, but sampled RGB was {averageR:F1}/{averageG:F1}/{averageB:F1}.");
    }

    [Fact]
    public void BundledDefaultPackedMetallicMapsStayNonMetallic()
    {
        foreach (string logicalPath in EnumerateBundledDefaultMaterialVariants())
        {
            string directory = Path.GetDirectoryName(logicalPath)?.Replace('\\', '/')
                ?? throw new InvalidOperationException($"Could not determine bundled texture directory for '{logicalPath}'.");
            string stem = Path.GetFileNameWithoutExtension(logicalPath);
            string metallicLogicalPath = $"{directory}/{stem[..^"_Color".Length]}_Metallic.png";

            BundledDefaultMaterialAssetStore assetStore = new();
            Assert.True(assetStore.Contains(metallicLogicalPath), $"Missing packed metallic map: {metallicLogicalPath}");

            using Stream stream = assetStore.OpenRead(metallicLogicalPath);
            using Image<Rgba32> image = Image.Load<Rgba32>(stream);
            for (int y = 0; y < image.Height; y += 32)
            {
                for (int x = 0; x < image.Width; x += 32)
                {
                    Rgba32 pixel = image[x, y];
                    if (!CanUseUpstreamMetalness(metallicLogicalPath))
                    {
                        Assert.True(
                            pixel.R == 0,
                            $"Expected no metalness in {metallicLogicalPath}, but sampled R={pixel.R} at {x},{y}.");
                    }

                    Assert.True(
                        pixel.A + pixel.B == byte.MaxValue,
                        $"Expected alpha smoothness to be inverse roughness in {metallicLogicalPath}, but sampled B={pixel.B}, A={pixel.A} at {x},{y}.");
                }
            }
        }
    }

    private static bool CanUseUpstreamMetalness(string metallicLogicalPath)
    {
        return metallicLogicalPath.Contains("/ambientcg/facade/Facade018A_", StringComparison.Ordinal)
            || metallicLogicalPath.Contains("/ambientcg/facade/Facade019A_", StringComparison.Ordinal)
            || metallicLogicalPath.Contains("/ambientcg/facade/Facade020A_", StringComparison.Ordinal);
    }

    private static string? GetPath(BundledDefaultTextureAsset? asset)
    {
        return asset is null
            ? null
            : asset.LogicalPath;
    }

    private static IEnumerable<string> EnumerateBundledDefaultMaterialVariants()
    {
        HashSet<string> variants = new(StringComparer.Ordinal);
        foreach (string family in new[]
        {
            BundledDefaultMaterialFamilies.Facade,
            BundledDefaultMaterialFamilies.Roof,
            BundledDefaultMaterialFamilies.RoadTriplanar,
            BundledDefaultMaterialFamilies.Vegetation,
            BundledDefaultMaterialFamilies.CityFurniture,
            BundledDefaultMaterialFamilies.Other,
        })
        {
            foreach (string variant in BundledDefaultMaterialFamilies.GetVariants(family))
            {
                if (variants.Add(variant))
                {
                    yield return variant;
                }
            }
        }
    }
}
