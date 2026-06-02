using System;

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
            new PresentationMaterialBinding(
                BaseColor: new ColorRgba(0.1, 0.2, 0.3, 0.4),
                MaterialType: MaterialType.Standard,
                TexturePayload: CreateEncodedTexturePayload(),
                TextureSourceKind: TextureSourceKind.Dataset,
                Projection: MaterialProjection.Uv,
                DepthOffset: new MaterialDepthOffset(-1.5, 2.5),
                SubmeshIndices: [0],
                TextureScale: new Float2(0.25, 0.5),
                Family: "roof",
                TextureOffset: new Float2(0.75, 0.125),
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
        Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, mapped.AssetScope);
        Assert.Equal(3, mapped.BundledVariantIndex);
    }

    [Fact]
    public void ToInternalMaterialBindingsUsesCommonScopeOnlyForTypedCommonMaterial()
    {
        DefaultCommonMaterialMember commonMaterial = CommonMaterialCatalog.Create().Generic.Uv;
        MaterialBinding[] bindings =
        [
            commonMaterial.CreateBinding([0]),
        ];

        ResoniteMaterialBinding mapped = Assert.Single(SceneImportContractMapper.ToInternal(bindings));

        Assert.Equal(ResoniteMaterialAssetScope.Common, mapped.AssetScope);
        Assert.Equal(commonMaterial, mapped.CommonMaterial);
    }

    [Fact]
    public void ToInternalMaterialBindingsMapsRawTextureDimensionsWithoutNullableGuards()
    {
        MaterialBinding[] bindings =
        [
            new PresentationMaterialBinding(
                BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
                MaterialType: MaterialType.Standard,
                TexturePayload: new RawRgba32TexturePayload(
                    width: 2,
                    height: 1,
                    colorProfile: "sRGB",
                    binaryPayload: [1, 2, 3, 4, 5, 6, 7, 8],
                    identity: "raw:texture"),
                TextureSourceKind: TextureSourceKind.Dataset,
                Projection: MaterialProjection.Uv,
                DepthOffset: null,
                SubmeshIndices: [0]),
        ];

        ResoniteMaterialBinding mapped = Assert.Single(SceneImportContractMapper.ToInternal(bindings));

        Assert.Equal(ResoniteTexturePayloadFormat.RawRgba32, mapped.TexturePayload!.Format);
        Assert.Equal(2, mapped.TexturePayload.Width);
        Assert.Equal(1, mapped.TexturePayload.Height);
        Assert.Equal("raw:texture", mapped.TexturePayload.Identity);
        Assert.Equal<byte>([1, 2, 3, 4, 5, 6, 7, 8], mapped.TexturePayload.BinaryPayload);
    }

    [Theory]
    [InlineData(nameof(MaterialBinding.MaterialType))]
    [InlineData(nameof(MaterialBinding.TextureSourceKind))]
    [InlineData(nameof(MaterialBinding.Projection))]
    public void ToInternalMaterialBindingsRejectsUnsupportedContractEnumValues(string invalidField)
    {
        MaterialBinding binding = CreateValidBinding() with
        {
            MaterialType = invalidField == nameof(MaterialBinding.MaterialType) ? (MaterialType)999 : MaterialType.Standard,
            TextureSourceKind = invalidField == nameof(MaterialBinding.TextureSourceKind) ? (TextureSourceKind)999 : TextureSourceKind.Dataset,
            Projection = invalidField == nameof(MaterialBinding.Projection) ? (MaterialProjection)999 : MaterialProjection.Uv,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => SceneImportContractMapper.ToInternal(binding));
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
            new SharedCommonMaterialBinding(
                BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
                MaterialType: MaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind: TextureSourceKind.Dataset,
                Projection: MaterialProjection.Uv,
                DepthOffset: null,
                SubmeshIndices: [0],
                commonMaterial,
                TerrainOverlayMaterial: new TerrainOverlayMaterialBinding(ThirdRegionalMeshCode.Parse("53394525"), overlay)),
        ];

        ResoniteMaterialBinding mapped = Assert.Single(SceneImportContractMapper.ToInternal(bindings));

        Assert.Equal(ResoniteMaterialAssetScope.Common, mapped.AssetScope);
        Assert.Same(overlay, mapped.TerrainOverlay);
        Assert.Equal("53394525", mapped.TerrainMeshCode);
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
            new PresentationMaterialBinding(
                BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
                MaterialType: MaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind: TextureSourceKind.Dataset,
                Projection: MaterialProjection.Uv,
                DepthOffset: null,
                SubmeshIndices: [0],
                TerrainOverlayMaterial: new TerrainOverlayMaterialBinding(ThirdRegionalMeshCode.Parse("53394525"), overlay)),
        ];

        ResoniteMaterialBinding mapped = Assert.Single(SceneImportContractMapper.ToInternal(bindings));

        Assert.Equal(ResoniteMaterialAssetScope.PresentationSlotScoped, mapped.AssetScope);
        Assert.Same(overlay, mapped.TerrainOverlay);
        Assert.Equal("53394525", mapped.TerrainMeshCode);
        Assert.Null(mapped.CommonMaterial);
    }

    private static MaterialBinding CreateValidBinding()
    {
        return new PresentationMaterialBinding(
            BaseColor: new ColorRgba(0.1, 0.2, 0.3, 0.4),
            MaterialType: MaterialType.Standard,
            TexturePayload: CreateEncodedTexturePayload(),
            TextureSourceKind: TextureSourceKind.Dataset,
            Projection: MaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0]);
    }

    private static TexturePayload CreateEncodedTexturePayload()
    {
        return new EncodedImageTexturePayload(
            width: 2,
            height: 2,
            colorProfile: "sRGB",
            source: TextureImportSourceFactory.CreateInMemoryEncodedImage(
                colorProfile: "sRGB",
                bytes: [1, 2, 3, 4],
                identity: "dataset:texture"));
    }
}
