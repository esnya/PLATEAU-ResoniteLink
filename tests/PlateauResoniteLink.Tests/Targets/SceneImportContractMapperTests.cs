using System.Collections.Generic;

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
                MaterialKey: "shared",
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

        Assert.Equal("shared", mapped.MaterialKey);
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
    public void ToInternalCityObjectPreservesFallbackRoofClassification()
    {
        ImportedCityObject cityObject = CreateImportedRoofOnlyBuilding(
            CreateRoofMaterialBinding("roof", [0]),
            ImportedCityObjectClassification.FallbackRoofBuilding);

        ResoniteConstructionCityObject mapped = SceneImportContractMapper.ToInternal(cityObject);

        Assert.True(mapped.UsesFallbackRoofStrategy);
    }

    [Fact]
    public void ToInternalCityObjectDoesNotInferFallbackRoofStrategyFromMaterialShape()
    {
        ImportedCityObject cityObject = CreateImportedRoofOnlyBuilding(
            CreateRoofMaterialBinding("roof", [0]),
            ImportedCityObjectClassification.Default);

        ResoniteConstructionCityObject mapped = SceneImportContractMapper.ToInternal(cityObject);

        Assert.False(mapped.UsesFallbackRoofStrategy);
    }

    [Fact]
    public void ToInternalCityObjectDoesNotSetFallbackRoofStrategyForMixedMaterials()
    {
        ImportedCityObject cityObject = new(
            ObjectKey: "building",
            DisplayName: "building",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Transform: new Transform3D(new Float3(0.0, 0.0, 0.0)),
            Mesh: new ImportedMesh(
                [
                    new MeshVertex(new Float3(0.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 0.0)),
                    new MeshVertex(new Float3(1.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(1.0, 0.0)),
                    new MeshVertex(new Float3(0.0, 0.0, 1.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 1.0)),
                    new MeshVertex(new Float3(1.0, 0.0, 1.0), new Float3(0.0, 1.0, 0.0), new Float2(1.0, 1.0)),
                ],
                [
                    new MeshSubmesh(0, "roof", [0, 1, 2]),
                    new MeshSubmesh(1, "facade", [1, 3, 2]),
                ]),
            Materials:
            [
                CreateRoofMaterialBinding("roof", [0]),
                new MaterialBinding(
                    MaterialKey: "facade",
                    BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
                    MaterialType: MaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: TextureSourceKind.Bundled,
                    Projection: MaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    TextureScale: new Float2(
                        BundledDefaultMaterialProfiles.FacadeDefaultTilesPerMeterValue.X,
                        BundledDefaultMaterialProfiles.FacadeDefaultTilesPerMeterValue.Y),
                    Family: BundledDefaultMaterialFamilies.Facade,
                    ReuseScope: MaterialReuseScope.Shared,
                    BundledVariantIndex: 0),
            ],
            Classification: ImportedCityObjectClassification.Default);

        ResoniteConstructionCityObject mapped = SceneImportContractMapper.ToInternal(cityObject);

        Assert.False(mapped.UsesFallbackRoofStrategy);
    }

    private static ImportedCityObject CreateImportedRoofOnlyBuilding(
        MaterialBinding material,
        ImportedCityObjectClassification classification)
    {
        return new ImportedCityObject(
            ObjectKey: "building",
            DisplayName: "building",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Transform: new Transform3D(new Float3(0.0, 0.0, 0.0)),
            Mesh: new ImportedMesh(
                [
                    new MeshVertex(new Float3(0.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 0.0)),
                    new MeshVertex(new Float3(1.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(1.0, 0.0)),
                    new MeshVertex(new Float3(0.0, 0.0, 1.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 1.0)),
                ],
                [
                    new MeshSubmesh(0, material.MaterialKey, [0, 1, 2]),
                ]),
            Materials: [material],
            Classification: classification);
    }

    private static MaterialBinding CreateRoofMaterialBinding(string materialKey, IReadOnlyList<int> submeshIndices)
    {
        return new MaterialBinding(
            MaterialKey: materialKey,
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            MaterialType: MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: TextureSourceKind.Bundled,
            Projection: MaterialProjection.Triplanar,
            DepthOffset: null,
            SubmeshIndices: submeshIndices,
            TextureScale: new Float2(
                BundledDefaultMaterialProfiles.RoofingTiles012ATilesPerMeterValue.X,
                BundledDefaultMaterialProfiles.RoofingTiles012ATilesPerMeterValue.Y),
            Family: BundledDefaultMaterialFamilies.Roof,
            ReuseScope: MaterialReuseScope.Shared,
            BundledVariantIndex: 2);
    }
}
