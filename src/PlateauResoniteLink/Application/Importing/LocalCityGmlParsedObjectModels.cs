using System.Collections.Generic;
using System.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record ParsedRing(
    string RingId,
    GeodeticPoint[] Vertices,
    IReadOnlyList<Float2>? UVs)
{
    internal LocalCityGmlObjectProjection.ParsedRing ToProjectionModel()
    {
        return new LocalCityGmlObjectProjection.ParsedRing(
            RingId,
            Vertices.Select(static point => point.ToProjectionModel()).ToArray(),
            UVs);
    }

    internal static ParsedRing FromProjectionModel(LocalCityGmlObjectProjection.ParsedRing ring)
    {
        return new ParsedRing(
            ring.RingId,
            ring.Vertices.Select(GeodeticPoint.FromProjectionModel).ToArray(),
            ring.UVs);
    }
}

internal enum ParsedSurfaceSemantic
{
    Unknown = 0,
    Wall = 1,
    Roof = 2,
    Ground = 3,
    Closure = 4,
    OuterCeiling = 5,
    OuterFloor = 6,
}

internal sealed record ParsedSurface(
    string PolygonId,
    ParsedSurfaceSemantic Semantic,
    ParsedRing ExteriorRing,
    ParsedRing[] InteriorRings,
    ColorRgba BaseColor,
    TexturePayload? TexturePayload,
    bool UsesGeneratedDemTexture = false,
    MaterialOpticalProperties? OpticalProperties = null)
{
    public IEnumerable<GeodeticPoint> Vertices =>
        ExteriorRing.Vertices.Concat(InteriorRings.SelectMany(static ring => ring.Vertices));

    internal LocalCityGmlObjectProjection.ParsedSurface ToProjectionModel()
    {
        return new LocalCityGmlObjectProjection.ParsedSurface(
            PolygonId,
            (LocalCityGmlObjectProjection.ParsedSurfaceSemantic)Semantic,
            ExteriorRing.ToProjectionModel(),
            InteriorRings.Select(static ring => ring.ToProjectionModel()).ToArray(),
            BaseColor,
            TexturePayload,
            UsesGeneratedDemTexture,
            OpticalProperties);
    }

    internal static ParsedSurface FromProjectionModel(LocalCityGmlObjectProjection.ParsedSurface surface)
    {
        return new ParsedSurface(
            surface.PolygonId,
            (ParsedSurfaceSemantic)surface.Semantic,
            ParsedRing.FromProjectionModel(surface.ExteriorRing),
            surface.InteriorRings.Select(ParsedRing.FromProjectionModel).ToArray(),
            new ColorRgba(surface.BaseColor.R, surface.BaseColor.G, surface.BaseColor.B, surface.BaseColor.A),
            surface.TexturePayload,
            surface.UsesGeneratedDemTexture,
            surface.OpticalProperties);
    }
}

internal sealed record ParsedCityObject(
    string SlotKey,
    string DisplayName,
    string PackageName,
    string ActualMeshCode,
    int? LodLevel,
    ParsedSurface[] Surfaces,
    CoordinateReferenceSystem ReferenceSystem,
    string SourceFileRelativePath,
    bool SharedAcrossMeshCodes,
    bool TerrainAligned = false,
    GeodeticPoint? GeodeticOriginOverride = null,
    int? FloorsAboveGround = null,
    double? MeasuredHeightMeters = null,
    BuildingAttributeContext? BuildingAttributes = null,
    double? GeometryHeightMeters = null)
{
    internal LocalCityGmlObjectProjection.ParsedCityObject ToProjectionModel()
    {
        return new LocalCityGmlObjectProjection.ParsedCityObject(
            SlotKey,
            DisplayName,
            PackageName,
            ActualMeshCode,
            LodLevel,
            Surfaces.Select(static surface => surface.ToProjectionModel()).ToArray(),
            ReferenceSystem.ToProjectionModel(),
            SourceFileRelativePath,
            SharedAcrossMeshCodes,
            TerrainAligned,
            GeodeticOriginOverride?.ToProjectionModel(),
            FloorsAboveGround,
            MeasuredHeightMeters,
            BuildingAttributes,
            GeometryHeightMeters);
    }

    internal static ParsedCityObject FromProjectionModel(LocalCityGmlObjectProjection.ParsedCityObject cityObject)
    {
        return new ParsedCityObject(
            cityObject.SlotKey,
            cityObject.DisplayName,
            cityObject.PackageName,
            cityObject.ActualMeshCode,
            cityObject.LodLevel,
            cityObject.Surfaces.Select(ParsedSurface.FromProjectionModel).ToArray(),
            CoordinateReferenceSystem.FromProjectionModel(cityObject.ReferenceSystem),
            cityObject.SourceFileRelativePath,
            cityObject.SharedAcrossMeshCodes,
            cityObject.TerrainAligned,
            cityObject.GeodeticOriginOverride is null ? null : GeodeticPoint.FromProjectionModel(cityObject.GeodeticOriginOverride),
            cityObject.FloorsAboveGround,
            cityObject.MeasuredHeightMeters,
            cityObject.BuildingAttributes,
            cityObject.GeometryHeightMeters);
    }
}
