using System;
using System.Linq;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ImportedDynamicMaterialUvNormalizerTests
{
    [Fact]
    public void ShouldNormalizeTextureTransform_ReturnsFalseForIdentityScaleWithoutOffset()
    {
        MaterialBinding material = CreateDynamicUvMaterial(
            textureScale: new Float2(1.0, 1.0),
            textureOffset: null);

        bool shouldNormalize = ImportedDynamicMaterialUvNormalizer.ShouldNormalizeTextureTransform(material);
        MaterialBinding normalized = ImportedDynamicMaterialUvNormalizer.NormalizeMaterialBinding(material);

        Assert.False(shouldNormalize);
        Assert.Null(normalized.TextureScale);
        Assert.Null(normalized.TextureOffset);
    }

    [Fact]
    public void Normalize_DoesNotExpandTriangleMeshForIdentityTransformOnlyMaterial()
    {
        ImportedCityObject cityObject = CreateTriangleCityObject(
            textureScale: new Float2(1.0, 1.0),
            textureOffset: null);

        ImportedCityObject normalized = ImportedDynamicMaterialUvNormalizer.Normalize(cityObject);

        Assert.Same(cityObject, normalized);
        Assert.Equal(3, normalized.Mesh.Vertices.Count);
        Assert.Equal(new Float2(1.0, 0.0), normalized.Mesh.Vertices[1].UV0);
    }

    [Fact]
    public void NormalizeMaterialBinding_ClearsBundledFamilyUvTransformAfterNormalization()
    {
        MaterialBinding material = new PresentationMaterialBinding(
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            MaterialType: MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: TextureSourceKind.Bundled,
            Projection: MaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: new Float2(1.0, 1.0),
            TextureOffset: new Float2(0.0, 0.0),
            Family: BundledDefaultMaterialFamilies.Facade,
            BundledVariantIndex: 0);

        MaterialBinding normalized = ImportedDynamicMaterialUvNormalizer.NormalizeMaterialBinding(material);

        Assert.Null(normalized.TextureScale);
        Assert.Null(normalized.TextureOffset);
    }

    [Fact]
    public void Normalize_NormalizesBundledFamilyUvTransformIntoMeshAndClearsMaterialTransform()
    {
        ImportedCityObject cityObject = new(
            ObjectKey: "mixed-material-city-object",
            DisplayName: "Mixed Material CityObject",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Transform: new Transform3D(new Float3(0.0, 0.0, 0.0)),
            Mesh: new ImportedMesh(
                [
                    new MeshVertex(new Float3(0.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 0.0)),
                    new MeshVertex(new Float3(1.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(1.0, 0.0)),
                    new MeshVertex(new Float3(0.0, 1.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 1.0)),
                    new MeshVertex(new Float3(2.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 0.0)),
                    new MeshVertex(new Float3(3.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(1.0, 0.0)),
                    new MeshVertex(new Float3(2.0, 1.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 1.0)),
                ],
                [
                    new MeshSubmesh(0, [0, 1, 2]),
                    new MeshSubmesh(1, [3, 4, 5]),
                ]),
            Materials:
            [
                CreateDynamicUvMaterial(new Float2(0.5, 0.25), new Float2(0.125, 0.75)) with
                {
                    SubmeshIndices = [0],
                },
                new PresentationMaterialBinding(
                    BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
                    MaterialType: MaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: TextureSourceKind.Bundled,
                    Projection: MaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    TextureScale: new Float2(1.0, 1.0),
                    TextureOffset: new Float2(0.0, 0.0),
                    Family: BundledDefaultMaterialFamilies.Facade,
                    BundledVariantIndex: 0),
            ],
            SourceFileRelativePath: "unit-a.gml");

        ImportedCityObject normalized = ImportedDynamicMaterialUvNormalizer.Normalize(cityObject);
        MaterialBinding bundledMaterial = Assert.Single(
            normalized.Materials,
            static material => string.Equals(material.Family, BundledDefaultMaterialFamilies.Facade, StringComparison.Ordinal)
                && material.BundledVariantIndex == 0
                && material.SubmeshIndices.SequenceEqual([1]));

        Assert.NotSame(cityObject, normalized);
        Assert.Null(bundledMaterial.TextureScale);
        Assert.Null(bundledMaterial.TextureOffset);
        Assert.Equal(new Float2(0.0, -0.5), normalized.Mesh.Vertices[3].UV0);
        Assert.Equal(new Float2(6.0, -0.5), normalized.Mesh.Vertices[4].UV0);
        Assert.Equal(new Float2(0.0, 5.5), normalized.Mesh.Vertices[5].UV0);
    }

    [Fact]
    public void Normalize_PreservesExplicitIdentityScaleForUnrelatedTriplanarMaterial()
    {
        ImportedCityObject cityObject = new(
            ObjectKey: "mixed-triplanar-city-object",
            DisplayName: "Mixed Triplanar CityObject",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Transform: new Transform3D(new Float3(0.0, 0.0, 0.0)),
            Mesh: new ImportedMesh(
                [
                    new MeshVertex(new Float3(0.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 0.0)),
                    new MeshVertex(new Float3(1.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(1.0, 0.0)),
                    new MeshVertex(new Float3(0.0, 1.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 1.0)),
                    new MeshVertex(new Float3(2.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 0.0)),
                    new MeshVertex(new Float3(3.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(1.0, 0.0)),
                    new MeshVertex(new Float3(2.0, 1.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 1.0)),
                ],
                [
                    new MeshSubmesh(0, [0, 1, 2]),
                    new MeshSubmesh(1, [3, 4, 5]),
                ]),
            Materials:
            [
                CreateDynamicUvMaterial(new Float2(0.5, 0.25), new Float2(0.125, 0.75)) with
                {
                    SubmeshIndices = [0],
                },
                new PresentationMaterialBinding(
                    BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
                    MaterialType: MaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: TextureSourceKind.Bundled,
                    Projection: MaterialProjection.Triplanar,
                    DepthOffset: null,
                    SubmeshIndices: [1],
                    TextureScale: new Float2(1.0, 1.0),
                    TextureOffset: new Float2(0.0, 0.0)),
            ],
            SourceFileRelativePath: "unit-a.gml");

        ImportedCityObject normalized = ImportedDynamicMaterialUvNormalizer.Normalize(cityObject);
        MaterialBinding triplanarMaterial = Assert.Single(
            normalized.Materials,
            static material => material.Projection == MaterialProjection.Triplanar
                && material.SubmeshIndices.SequenceEqual([1]));

        Assert.NotSame(cityObject, normalized);
        Assert.Equal(MaterialProjection.Triplanar, triplanarMaterial.Projection);
        Assert.Equal(new Float2(1.0, 1.0), triplanarMaterial.TextureScale);
        Assert.Equal(new Float2(0.0, 0.0), triplanarMaterial.TextureOffset);
    }

    [Fact]
    public void Normalize_NormalizesTerrainOverlayTextureTransformIntoMeshUv()
    {
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 17,
            GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
            MaxTextureSize: 512);
        ImportedCityObject cityObject = CreateTriangleCityObject(
            new Float2(0.5, 0.25),
            new Float2(0.125, 0.75),
            overlay);

        ImportedCityObject normalized = ImportedDynamicMaterialUvNormalizer.Normalize(cityObject);

        MaterialBinding material = Assert.Single(normalized.Materials);
        Assert.Null(material.TextureScale);
        Assert.Null(material.TextureOffset);
        Assert.Equal(new Float2(0.125, 0.75), normalized.Mesh.Vertices[0].UV0);
        Assert.Equal(new Float2(0.625, 0.75), normalized.Mesh.Vertices[1].UV0);
        Assert.Equal(new Float2(0.125, 1.0), normalized.Mesh.Vertices[2].UV0);
    }

    [Fact]
    public void Normalize_AssignsGenericCommonMaterialAfterDatasetAlbedoUvTransformIsBaked()
    {
        ImportedCityObject cityObject = CreateTriangleCityObject(
            new Float2(0.5, 0.25),
            new Float2(0.125, 0.75));

        ImportedCityObject normalized = ImportedDynamicMaterialUvNormalizer.Normalize(cityObject);

        MaterialBinding material = Assert.Single(normalized.Materials);
        Assert.Null(material.TextureScale);
        Assert.Null(material.TextureOffset);
        Assert.Equal(CommonMaterialCatalog.Create().Generic.Uv, material.CommonMaterial);
    }

    [Fact]
    public void Normalize_AssignsGenericCommonMaterialToTerrainOverlayAfterUvTransformIsBaked()
    {
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 17,
            GeographicBounds: new GeographicRectangle(35.68, 35.69, 139.69, 139.70),
            MaxTextureSize: 512);
        ImportedCityObject cityObject = CreateTriangleCityObject(
            new Float2(0.5, 0.25),
            new Float2(0.125, 0.75),
            overlay);

        ImportedCityObject normalized = ImportedDynamicMaterialUvNormalizer.Normalize(cityObject);

        MaterialBinding material = Assert.Single(normalized.Materials);
        Assert.Equal(CommonMaterialCatalog.Create().Generic.Uv, material.CommonMaterial);
    }

    private static ImportedCityObject CreateTriangleCityObject(
        Float2? textureScale,
        Float2? textureOffset,
        TerrainTextureOverlay? terrainOverlay = null)
    {
        return new ImportedCityObject(
            ObjectKey: "dynamic-uv-city-object",
            DisplayName: "Dynamic UV CityObject",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Transform: new Transform3D(new Float3(0.0, 0.0, 0.0)),
            Mesh: new ImportedMesh(
                [
                    new MeshVertex(new Float3(0.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 0.0)),
                    new MeshVertex(new Float3(1.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(1.0, 0.0)),
                    new MeshVertex(new Float3(0.0, 1.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 1.0)),
                ],
                [
                    new MeshSubmesh(0, [0, 1, 2]),
                ]),
            Materials:
            [
                CreateDynamicUvMaterial(textureScale, textureOffset, terrainOverlay),
            ],
            SourceFileRelativePath: "unit-a.gml");
    }

    private static MaterialBinding CreateDynamicUvMaterial(
        Float2? textureScale,
        Float2? textureOffset,
        TerrainTextureOverlay? terrainOverlay = null)
    {
        return new PresentationMaterialBinding(
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            MaterialType: MaterialType.Standard,
            TexturePayload: new RawRgba32TexturePayload(1, 1, "srgb", [255, 255, 255, 255], "textures/dynamic-uv.png"),
            TextureSourceKind: TextureSourceKind.Dataset,
            Projection: MaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: textureScale,
            TextureOffset: textureOffset,
            TerrainOverlayMaterial: terrainOverlay is null
                ? null
                : new TerrainOverlayMaterialBinding(terrainOverlay.MeshCode, terrainOverlay));
    }
}
