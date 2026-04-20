using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Targets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteDynamicMaterialUvNormalizerTests
{
    [Fact]
    public void ShouldBakeTextureTransform_ReturnsFalseForIdentityScaleWithoutOffset()
    {
        ResoniteMaterialBinding material = CreateDynamicUvMaterial(
            textureScale: new ResoniteFloat2(1.0, 1.0),
            textureOffset: null);

        bool shouldBake = ResoniteDynamicMaterialUvNormalizer.ShouldBakeTextureTransform(material);
        ResoniteMaterialBinding normalized = ResoniteDynamicMaterialUvNormalizer.NormalizeMaterialBinding(material);

        Assert.False(shouldBake);
        Assert.Null(normalized.TextureScale);
        Assert.Null(normalized.TextureOffset);
    }

    [Fact]
    public void Normalize_DoesNotExpandTriangleMeshForIdentityTransformOnlyMaterial()
    {
        ResoniteConstructionCityObject cityObject = CreateTriangleCityObject(
            textureScale: new ResoniteFloat2(1.0, 1.0),
            textureOffset: null);

        ResoniteConstructionCityObject normalized = ResoniteDynamicMaterialUvNormalizer.Normalize(cityObject);

        Assert.Same(cityObject, normalized);
        Assert.Equal(3, normalized.Mesh.Vertices.Count);
        Assert.Equal(new ResoniteFloat2(1.0, 0.0), normalized.Mesh.Vertices[1].UV0);
    }

    [Fact]
    public void NormalizeMaterialBinding_PreservesExplicitIdentityScaleForBundledFamilyMaterial()
    {
        ResoniteMaterialBinding material = new(
            MaterialKey: "bundled-identity-override",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new ResoniteFloat2(1.0, 1.0),
            TextureOffset: new ResoniteFloat2(0.0, 0.0),
            Family: BundledDefaultMaterialFamilies.Facade,
            BundledVariantIndex: 0,
            AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);

        ResoniteMaterialBinding normalized = ResoniteDynamicMaterialUvNormalizer.NormalizeMaterialBinding(material);

        Assert.Equal(new ResoniteFloat2(1.0, 1.0), normalized.TextureScale);
        Assert.Null(normalized.TextureOffset);
    }

    [Fact]
    public void Normalize_PreservesBundledIdentityScaleOverrideWhenAnotherMaterialBakes()
    {
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "mixed-material-city-object",
            DisplayName: "Mixed Material CityObject",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(2.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(3.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(2.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(0, "dynamic-uv-material", [0, 1, 2]),
                    new ResoniteMeshSubmesh(1, "bundled-identity-override", [3, 4, 5]),
                ]),
            Materials:
            [
                CreateDynamicUvMaterial(new ResoniteFloat2(0.5, 0.25), new ResoniteFloat2(0.125, 0.75)) with
                {
                    SubmeshIndices = [0],
                },
                new ResoniteMaterialBinding(
                    MaterialKey: "bundled-identity-override",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    TextureScale: new ResoniteFloat2(1.0, 1.0),
                    TextureOffset: new ResoniteFloat2(0.0, 0.0),
                    Family: BundledDefaultMaterialFamilies.Facade,
                    BundledVariantIndex: 0,
                    AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped),
            ],
            SourceObjectKey: "unit-a:mixed-material-city-object",
            SourceUnitKey: "unit-a",
            SourceFileRelativePath: "unit-a.gml");

        ResoniteConstructionCityObject normalized = ResoniteDynamicMaterialUvNormalizer.Normalize(cityObject);
        ResoniteMaterialBinding bundledMaterial = Assert.Single(
            normalized.Materials,
            static material => string.Equals(material.MaterialKey, "bundled-identity-override", StringComparison.Ordinal));

        Assert.NotSame(cityObject, normalized);
        Assert.Equal(new ResoniteFloat2(1.0, 1.0), bundledMaterial.TextureScale);
        Assert.Null(bundledMaterial.TextureOffset);
    }

    [Fact]
    public void Normalize_PreservesExplicitIdentityScaleForUnrelatedTriplanarMaterial()
    {
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "mixed-triplanar-city-object",
            DisplayName: "Mixed Triplanar CityObject",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(2.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(3.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(2.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(0, "dynamic-uv-material", [0, 1, 2]),
                    new ResoniteMeshSubmesh(1, "triplanar-identity-override", [3, 4, 5]),
                ]),
            Materials:
            [
                CreateDynamicUvMaterial(new ResoniteFloat2(0.5, 0.25), new ResoniteFloat2(0.125, 0.75)) with
                {
                    SubmeshIndices = [0],
                },
                new ResoniteMaterialBinding(
                    MaterialKey: "triplanar-identity-override",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Triplanar,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    TextureScale: new ResoniteFloat2(1.0, 1.0),
                    TextureOffset: new ResoniteFloat2(0.0, 0.0),
                    Family: null,
                    BundledVariantIndex: null,
                    AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped),
            ],
            SourceObjectKey: "unit-a:mixed-triplanar-city-object",
            SourceUnitKey: "unit-a",
            SourceFileRelativePath: "unit-a.gml");

        ResoniteConstructionCityObject normalized = ResoniteDynamicMaterialUvNormalizer.Normalize(cityObject);
        ResoniteMaterialBinding triplanarMaterial = Assert.Single(
            normalized.Materials,
            static material => string.Equals(material.MaterialKey, "triplanar-identity-override", StringComparison.Ordinal));

        Assert.NotSame(cityObject, normalized);
        Assert.Equal(ResoniteMaterialProjection.Triplanar, triplanarMaterial.Projection);
        Assert.Equal(new ResoniteFloat2(1.0, 1.0), triplanarMaterial.TextureScale);
        Assert.Equal(new ResoniteFloat2(0.0, 0.0), triplanarMaterial.TextureOffset);
    }

    private static ResoniteConstructionCityObject CreateTriangleCityObject(
        ResoniteFloat2? textureScale,
        ResoniteFloat2? textureOffset)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: "dynamic-uv-city-object",
            DisplayName: "Dynamic UV CityObject",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(0, "dynamic-uv-material", [0, 1, 2]),
                ]),
            Materials:
            [
                CreateDynamicUvMaterial(textureScale, textureOffset),
            ],
            SourceObjectKey: "unit-a:dynamic-uv-city-object",
            SourceUnitKey: "unit-a",
            SourceFileRelativePath: "unit-a.gml");
    }

    private static ResoniteMaterialBinding CreateDynamicUvMaterial(
        ResoniteFloat2? textureScale,
        ResoniteFloat2? textureOffset)
    {
        return new ResoniteMaterialBinding(
            MaterialKey: "dynamic-uv-material",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: new ResoniteTexturePayload(1, 1, "srgb", [255, 255, 255, 255], "textures/dynamic-uv.png"),
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: textureScale,
            TextureOffset: textureOffset,
            AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);
    }
}
