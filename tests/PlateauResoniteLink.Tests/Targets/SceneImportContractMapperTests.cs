using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class SceneImportContractMapperTests
{
    [Fact]
    public void ToInternalMaterialBindingsPreservesNeutralContractFields()
    {
        MaterialBinding[] bindings =
        [
            new(
                BaseColor: new ColorRgba(0.1, 0.2, 0.3, 0.4),
                MaterialType: MaterialType.Standard,
                TexturePayload: new TexturePayload(2, 2, "sRGB", [1, 2, 3, 4], "dataset:texture", TexturePayloadFormat.EncodedImage),
                TextureSourceKind: TextureSourceKind.Dataset,
                Projection: MaterialProjection.Uv,
                DepthOffset: new MaterialDepthOffset(-1.5, 2.5),
                SubmeshIndices: [0],
                TextureScale: new Float2(0.25, 0.5),
                Family: "roof",
                TextureOffset: new Float2(0.75, 0.125),
                ReuseScope: MaterialReuseScope.Shared,
                BundledVariantIndex: 3),
        ];

        ResoniteMaterialBinding mapped = Assert.Single(SceneImportContractMapper.ToInternal(bindings));

        Assert.Equal(0.1, mapped.BaseColor.R, 9);
        Assert.Equal(0.2, mapped.BaseColor.G, 9);
        Assert.Equal("dataset:texture", mapped.TexturePayload!.Identity);
        Assert.Equal(ResoniteTexturePayloadFormat.EncodedImage, mapped.TexturePayload.Format);
        Assert.Equal(-1.5, mapped.DepthOffset!.Factor, 9);
        Assert.Equal(2.5, mapped.DepthOffset.Units, 9);
        Assert.Equal(0.25, mapped.TextureScale!.X, 9);
        Assert.Equal(0.125, mapped.TextureOffset!.Y, 9);
        Assert.Equal(ResoniteMaterialAssetScope.Common, mapped.AssetScope);
        Assert.Equal(3, mapped.BundledVariantIndex);
    }

    [Fact]
    public void ToInternalMaterialBindingsKeepsTerrainOverlaySharedScopeIndependentFromProvider()
    {
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: new GeographicRectangle(35.0, 35.01, 139.0, 139.01),
            MaxTextureSize: 512,
            Sources:
            [
                new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 17),
            ]);
        DefaultCommonMaterialMember commonMaterial = CommonMaterialCatalog.Create().Generic.Uv;
        MaterialBinding[] bindings =
        [
            new(
                BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
                MaterialType: MaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind: TextureSourceKind.Dataset,
                Projection: MaterialProjection.Uv,
                DepthOffset: null,
                SubmeshIndices: [0],
                ReuseScope: MaterialReuseScope.Shared,
                TerrainOverlayMaterial: new TerrainOverlayMaterialBinding(ThirdRegionalMeshCode.Parse("53394525"), overlay),
                CommonMaterial: commonMaterial),
        ];

        ResoniteMaterialBinding mapped = Assert.Single(SceneImportContractMapper.ToInternal(bindings));

        Assert.Equal(ResoniteMaterialAssetScope.Common, mapped.AssetScope);
        Assert.Same(overlay, mapped.TerrainOverlay);
        Assert.Equal("53394525", mapped.TerrainMeshCode);
        Assert.Equal(commonMaterial, mapped.CommonMaterial);
    }

    [Fact]
    public void CommonMaterialMemberCreatesTypedSharedCommonBinding()
    {
        DefaultCommonMaterialMember commonMaterial = CommonMaterialCatalog.Create().Generic.Uv;

        MaterialBinding binding = commonMaterial.CreateBinding([0]);
        ResoniteMaterialBinding mapped = SceneImportContractMapper.ToInternal(binding);

        Assert.IsType<SharedCommonMaterialBinding>(binding);
        Assert.Equal(MaterialReuseScope.Shared, binding.ReuseScope);
        Assert.Equal(commonMaterial, binding.CommonMaterial);
        Assert.Equal(ResoniteMaterialAssetScope.Common, mapped.AssetScope);
        Assert.Equal(commonMaterial, mapped.CommonMaterial);
    }

    [Fact]
    public void PresentationCommonBindingKeepsPresentationScope()
    {
        DefaultCommonMaterialMember commonMaterial = CommonMaterialCatalog.Create().Generic.Uv;
        MaterialBinding binding = new PresentationCommonMaterialBinding(
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            MaterialType: MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: TextureSourceKind.Dataset,
            Projection: MaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            commonMaterial: commonMaterial);

        ResoniteMaterialBinding mapped = SceneImportContractMapper.ToInternal(binding);

        Assert.Equal(MaterialReuseScope.PerObject, binding.ReuseScope);
        Assert.Equal(commonMaterial, binding.CommonMaterial);
        Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, mapped.AssetScope);
        Assert.Equal(commonMaterial, mapped.CommonMaterial);
    }

    [Fact]
    public void ToInternalMaterialBindingsKeepsSharedTerrainOverlayWithoutCommonMaterialPresentationScoped()
    {
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: new GeographicRectangle(35.0, 35.01, 139.0, 139.01),
            MaxTextureSize: 512,
            Sources:
            [
                new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 17),
            ]);
        MaterialBinding[] bindings =
        [
            new(
                BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
                MaterialType: MaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind: TextureSourceKind.Dataset,
                Projection: MaterialProjection.Uv,
                DepthOffset: null,
                SubmeshIndices: [0],
                ReuseScope: MaterialReuseScope.Shared,
                TerrainOverlayMaterial: new TerrainOverlayMaterialBinding(ThirdRegionalMeshCode.Parse("53394525"), overlay)),
        ];

        ResoniteMaterialBinding mapped = Assert.Single(SceneImportContractMapper.ToInternal(bindings));

        Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, mapped.AssetScope);
        Assert.Same(overlay, mapped.TerrainOverlay);
        Assert.Equal("53394525", mapped.TerrainMeshCode);
        Assert.Null(mapped.CommonMaterial);
    }
}
