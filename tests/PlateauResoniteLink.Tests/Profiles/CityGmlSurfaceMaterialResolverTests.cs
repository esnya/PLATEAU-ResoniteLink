using System;
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
            SharedAcrossMeshCodes: false);

        ResolvedSurfaceMaterial? representativeSurface = CityGmlSurfaceMaterialResolver.EnumerateSurfaces(
                cityObject,
                cityObjectOrigin: new GeodeticPoint(35.0, 139.0, 0.0),
                cityObjectCartesian: null,
                demTerrainTextureOverlay: CreateOverlay("53394525"),
                materialResolver)
            .FirstOrDefault(static resolvedSurface => resolvedSurface.Surface.UsesGeneratedDemTexture);

        Assert.NotNull(representativeSurface);
        Assert.Equal(0, materialResolver.InvocationCount);
    }

    [Fact]
    public void CreateMaterialBindingReportsMissingRequestedMeshCodeWhenOverlayDoesNotMatchActualMeshCode()
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
            DepthOffset: null);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CityGmlSurfaceMaterialResolver.CreateMaterialBinding(
                "53394600",
                representativeSurface,
                materialIndex: 0));

        Assert.Contains("phase='material-binding'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("actual_mesh_code='53394600'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("requested_mesh_code=", exception.Message, StringComparison.Ordinal);
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

    private static TerrainTextureOverlay CreateOverlay(string meshCode)
    {
        Assert.True(PlateauMeshCode.TryGetBounds(
            meshCode,
            out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds));

        return new TerrainTextureOverlay(
            PackageName: "dem",
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
