using System.Linq;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class CityGmlSurfaceMaterialResolverTests
{
    [Fact]
    public void EnumerateSurfacesPreservesLazyScanAfterFirstGeneratedDemSurface()
    {
        CountingDefaultMaterialResolver materialResolver = new();
        ParsedCityObject cityObject = new(
            SlotKey: "dem-object",
            DisplayName: "dem-object",
            PackageName: "dem",
            ActualMeshCode: "53394525",
            LodLevel: null,
            Surfaces:
            [
                CreateParsedSurface("generated-dem", usesGeneratedDemTexture: true),
                CreateParsedSurface("ordinary-dem", usesGeneratedDemTexture: false),
            ],
            ReferenceSystem: CoordinateReferenceSystem.Parse("EPSG:4326"),
            SourceFileRelativePath: "udx/dem/53394525/sample.gml",
            SharedAcrossMeshCodes: false,
            BuildingAttributes: BuildingAttributeContext.Empty);

        ResolvedSurfaceMaterial? representativeSurface = CityGmlSurfaceMaterialResolver.EnumerateSurfaces(
                ConstructionCityObjectDraft.FromParsedCityObject(cityObject),
                cityObjectOrigin: new GeodeticPoint(35.0, 139.0, 0.0),
                cityObjectCartesian: null,
                demTerrainTextureOverlay: CreateOverlay("53394525"),
                materialResolver)
            .FirstOrDefault(static resolvedSurface => resolvedSurface.Surface.UsesGeneratedDemTexture);

        Assert.NotNull(representativeSurface);
        Assert.Equal(0, materialResolver.InvocationCount);
    }

    [Fact]
    public void CreateMaterialBindingUsesTerrainOverlayMeshCodeWithoutMatchingActualMeshCode()
    {
        ResolvedSurfaceMaterial representativeSurface = new(
            CreateSurface(),
            new ResolvedMaterial(
                MaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind.Bundled,
                MaterialProjection.Uv,
                Family: "terrain",
                TextureScale: null,
                MaterialReuseScope.Shared,
                TerrainOverlay: CreateOverlay("53394525")),
            depthOffset: null);

        MaterialBinding binding = CityGmlSurfaceMaterialResolver.CreateMaterialBinding(
            "53394600",
            representativeSurface,
            materialIndex: 0);

        Assert.Equal("53394525", binding.TerrainMeshCode);
        Assert.Same(representativeSurface.Material.TerrainOverlay, binding.TerrainOverlay);
    }

    [Fact]
    public void EnumerateSurfacesUsesConstructionRoleInsteadOfParsedSemanticForGeneratedNoWallSlabParts()
    {
        ParsedSurface generatedSide = CreateSurface(
            "lod2-roof_generated_no-wall-side-0",
            ParsedSurfaceSemantic.Wall);
        ParsedCityObject cityObject = CreateBuildingCityObject([generatedSide]);
        DefaultMaterialResolver materialResolver = new(CommonMaterialCatalog.Create());

        ResolvedSurfaceMaterial resolvedSurface = Assert.Single(CityGmlSurfaceMaterialResolver.ResolveSurfaces(
            ConstructionCityObjectDraft.FromParsedCityObject(cityObject),
            cityObjectOrigin: new GeodeticPoint(35.0, 139.0, 0.0),
            cityObjectCartesian: null,
            demTerrainTextureOverlay: null,
            materialResolver));

        Assert.Equal(ConstructionFaceRole.RoofSlab, resolvedSurface.Role);
        Assert.Equal(BundledDefaultMaterialFamilies.Roof, resolvedSurface.Material.Family);
        Assert.DoesNotContain(BundledDefaultMaterialFamilies.BuildingFacadeFamilies, family => family == resolvedSurface.Material.Family);
    }

    [Fact]
    public void EnumerateSurfacesMapsOrdinaryBuildingWallToFacadeConstructionRole()
    {
        ParsedSurface wall = CreateSurface("lod2-wall", ParsedSurfaceSemantic.Wall);
        ParsedCityObject cityObject = CreateBuildingCityObject([wall]);
        DefaultMaterialResolver materialResolver = new(CommonMaterialCatalog.Create());

        ResolvedSurfaceMaterial resolvedSurface = Assert.Single(CityGmlSurfaceMaterialResolver.ResolveSurfaces(
            ConstructionCityObjectDraft.FromParsedCityObject(cityObject),
            cityObjectOrigin: new GeodeticPoint(35.0, 139.0, 0.0),
            cityObjectCartesian: null,
            demTerrainTextureOverlay: null,
            materialResolver));

        Assert.Equal(ConstructionFaceRole.Wall, resolvedSurface.Role);
        Assert.Contains(BundledDefaultMaterialFamilies.BuildingFacadeFamilies, family => family == resolvedSurface.Material.Family);
    }

    private static ParsedSurface CreateParsedSurface(string polygonId, bool usesGeneratedDemTexture)
    {
        return new ParsedSurface(
            PolygonId: polygonId,
            Semantic: ParsedSurfaceSemantic.Ground,
            ExteriorRing: new ParsedRing(
                $"{polygonId}-ring",
                [
                    new GeodeticPoint(35.0, 139.0, 10.0),
                    new GeodeticPoint(35.0, 139.1, 10.0),
                    new GeodeticPoint(35.1, 139.1, 10.0),
                ],
                UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null,
            UsesGeneratedDemTexture: usesGeneratedDemTexture);
    }

    private static ParsedSurface CreateSurface()
    {
        return new ParsedSurface(
            PolygonId: "surface",
            Semantic: ParsedSurfaceSemantic.Roof,
            ExteriorRing: new ParsedRing(
                "surface-ring",
                [
                    new GeodeticPoint(35.0, 139.0, 10.0),
                    new GeodeticPoint(35.0, 139.1, 10.0),
                    new GeodeticPoint(35.1, 139.1, 10.0),
                ],
                UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);
    }

    private static ParsedSurface CreateSurface(string polygonId, ParsedSurfaceSemantic semantic)
    {
        ParsedSurface surface = CreateSurface();
        return surface with
        {
            PolygonId = polygonId,
            Semantic = semantic,
            ExteriorRing = surface.ExteriorRing with { RingId = $"{polygonId}-ring" },
        };
    }

    private static ParsedCityObject CreateBuildingCityObject(ParsedSurface[] surfaces)
    {
        return new ParsedCityObject(
            SlotKey: "bldg-object",
            DisplayName: "bldg-object",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Surfaces: surfaces,
            ReferenceSystem: CoordinateReferenceSystem.Parse("EPSG:4326"),
            SourceFileRelativePath: "udx/bldg/53394525/sample.gml",
            SharedAcrossMeshCodes: false,
            BuildingAttributes: BuildingAttributeContext.Empty);
    }

    private static TerrainTextureOverlay CreateOverlay(string meshCode)
    {
        Assert.True(PlateauMeshCode.TryGetBounds(
            meshCode,
            out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds));

        return new TerrainTextureOverlay(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse(meshCode),
            UrlTemplate: $"https://terrain.example/{meshCode}/{{z}}/{{x}}/{{y}}.png",
            ZoomLevel: 18,
            GeographicBounds: new GeographicRectangle(
                bounds.SouthLatitude,
                bounds.NorthLatitude,
                bounds.WestLongitude,
                bounds.EastLongitude),
            MaxTextureSize: 2048);
    }

    private sealed class CountingDefaultMaterialResolver : IDefaultMaterialResolver
    {
        public int InvocationCount { get; private set; }

        public ResolvedMaterial ResolveMaterial(DefaultMaterialRequest request)
        {
            InvocationCount++;
            return new ResolvedMaterial(
                MaterialType.Standard,
                TexturePayload: null,
                TextureSourceKind.Bundled,
                MaterialProjection.Uv,
                Family: null,
                TextureScale: null,
                MaterialReuseScope.PerObject);
        }
    }
}
