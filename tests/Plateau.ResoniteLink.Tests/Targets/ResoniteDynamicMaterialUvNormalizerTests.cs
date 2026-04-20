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
