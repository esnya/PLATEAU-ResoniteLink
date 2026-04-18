using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Tests.Targets;

public sealed class ResoniteMaterialComponentBuilderTests
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
            BundledVariantIndex: 0);

        string componentType = ResoniteMaterialComponentBuilder.GetComponentType(material);
        Dictionary<string, Member> members = ResoniteMaterialComponentBuilder.CreateMembers(material);

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
        Assert.Equal(0.5f, textureScale.Value.x, 6);
        Assert.Equal(0.25f, textureScale.Value.y, 6);
        Assert.Equal(0.125f, textureOffset.Value.x, 6);
        Assert.Equal(0.75f, textureOffset.Value.y, 6);
        Assert.Equal(2.0f, offsetFactor.Value, 6);
        Assert.Equal(3.0f, offsetUnits.Value, 6);
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

        Dictionary<string, Member> triplanarMembers = ResoniteMaterialComponentBuilder.CreateMembers(triplanarMaterial);
        Dictionary<string, Member> wireframeMembers = ResoniteMaterialComponentBuilder.CreateMembers(wireframeMaterial);

        Assert.Equal("[FrooxEngine]FrooxEngine.PBS_TriplanarMetallic", ResoniteMaterialComponentBuilder.GetComponentType(triplanarMaterial));
        Field_float2 triplanarTextureScale = Assert.IsType<Field_float2>(triplanarMembers["TextureScale"]);
        Field_float2 triplanarTextureOffset = Assert.IsType<Field_float2>(triplanarMembers["TextureOffset"]);
        Assert.IsType<Field_float>(triplanarMembers["Metallic"]);
        Assert.IsType<Field_float>(triplanarMembers["TriplanarBlendPower"]);
        Assert.IsType<Field_bool>(triplanarMembers["ObjectSpace"]);
        Assert.Equal(0.25f, triplanarTextureScale.Value.x, 6);
        Assert.Equal(0.125f, triplanarTextureScale.Value.y, 6);
        Assert.Equal(0.0f, triplanarTextureOffset.Value.x, 6);
        Assert.Equal(0.0f, triplanarTextureOffset.Value.y, 6);

        Assert.Equal("[FrooxEngine]FrooxEngine.WireframeMaterial", ResoniteMaterialComponentBuilder.GetComponentType(wireframeMaterial));
        Field_float thickness = Assert.IsType<Field_float>(wireframeMembers["Thickness"]);
        Field_colorX fillColor = Assert.IsType<Field_colorX>(wireframeMembers["FillColor"]);
        Assert.Equal(0.01f, thickness.Value, 6);
        Assert.Equal(0.04f, fillColor.Value.a, 6);
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

        bool resolved = ResoniteMaterialComponentBuilder.TryGetBundledCompanionTextureSet(material, out BundledDefaultMaterialTextureSet? textureSet);

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

        bool resolved = ResoniteMaterialComponentBuilder.TryGetBundledCompanionTextureSet(material, out BundledDefaultMaterialTextureSet? textureSet);

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

        bool resolved = BundledDefaultMaterialAssetStore.TryGetAbsolutePath(logicalPath, out string absolutePath);

        Assert.True(resolved);
        Assert.EndsWith("Plaster002_2K-JPG_Color.jpg", absolutePath, StringComparison.Ordinal);
        Assert.True(File.Exists(absolutePath));
    }

    [Fact]
    public void BundledDefaultCityFurnitureAlbedoStaysNearNeutralWhite()
    {
        string logicalPath = BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.CityFurniture, 0);
        Assert.True(BundledDefaultMaterialAssetStore.TryGetAbsolutePath(logicalPath, out string absolutePath));

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
