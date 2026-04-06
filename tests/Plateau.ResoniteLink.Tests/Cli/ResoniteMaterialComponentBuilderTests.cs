using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ResoniteMaterialComponentBuilderTests
{
    [Fact]
    public void CreateMembersBuildsUvStandardMaterialFields()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "facade",
            BaseColor: new ResoniteColor(0.1, 0.2, 0.3, 0.4),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePath: "facade/Facade018A_2K-JPG_Color.jpg",
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: new ResoniteMaterialDepthOffset(2.0, 3.0),
            TextureScale: new ResoniteFloat2(0.5, 0.25),
            SubmeshIndices: [0]);

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
        Assert.Equal(0.0f, textureOffset.Value.x, 6);
        Assert.Equal(0.0f, textureOffset.Value.y, 6);
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
            TexturePath: "road/Asphalt020L_2K-JPG_Color.jpg",
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Triplanar,
            DepthOffset: null,
            TextureScale: null,
            SubmeshIndices: [0]);
        ResoniteMaterialBinding wireframeMaterial = new(
            MaterialKey: "overlay",
            BaseColor: new ResoniteColor(0.2, 0.4, 0.6, 0.5),
            MaterialType: ResoniteMaterialType.Wireframe,
            TexturePath: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            TextureScale: null,
            SubmeshIndices: [0]);

        Dictionary<string, Member> triplanarMembers = ResoniteMaterialComponentBuilder.CreateMembers(triplanarMaterial);
        Dictionary<string, Member> wireframeMembers = ResoniteMaterialComponentBuilder.CreateMembers(wireframeMaterial);

        Assert.Equal("[FrooxEngine]FrooxEngine.PBS_TriplanarMetallic", ResoniteMaterialComponentBuilder.GetComponentType(triplanarMaterial));
        Assert.IsType<Field_float2>(triplanarMembers["TextureScale"]);
        Assert.IsType<Field_float2>(triplanarMembers["TextureOffset"]);
        Assert.IsType<Field_float>(triplanarMembers["Metallic"]);
        Assert.IsType<Field_float>(triplanarMembers["TriplanarBlendPower"]);
        Assert.IsType<Field_bool>(triplanarMembers["ObjectSpace"]);

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
            TexturePath: BundledDefaultMaterialFamilies.FacadeVariants[0],
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            TextureScale: null,
            SubmeshIndices: [0]);

        bool resolved = ResoniteMaterialComponentBuilder.TryGetBundledCompanionTextureSet(material, out BundledDefaultMaterialTextureSet? textureSet);

        Assert.True(resolved);
        Assert.NotNull(textureSet);
        Assert.EndsWith("_Emission.jpg", textureSet.EmissionPath, StringComparison.Ordinal);
        Assert.EndsWith("_Height.jpg", textureSet.HeightPath, StringComparison.Ordinal);
        Assert.EndsWith("_Metallic.png", textureSet.MetallicPath, StringComparison.Ordinal);
        Assert.EndsWith("_NormalGL.jpg", textureSet.NormalPath, StringComparison.Ordinal);
    }
}
