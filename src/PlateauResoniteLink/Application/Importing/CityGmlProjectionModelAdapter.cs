using System.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlProjectionModelAdapter
{
    internal static LocalCityGmlObjectProjection.ParsedRing ToProjectionModel(ParsedRing ring)
    {
        return new LocalCityGmlObjectProjection.ParsedRing(
            ring.RingId,
            ring.Vertices.Select(static point => point.ToProjectionModel()).ToArray(),
            ring.UVs);
    }

    internal static ParsedRing FromProjectionModel(LocalCityGmlObjectProjection.ParsedRing ring)
    {
        return new ParsedRing(
            ring.RingId,
            ring.Vertices.Select(GeodeticPoint.FromProjectionModel).ToArray(),
            ring.UVs);
    }

    internal static LocalCityGmlObjectProjection.ParsedSurface ToProjectionModel(ParsedSurface surface)
    {
        return new LocalCityGmlObjectProjection.ParsedSurface(
            surface.PolygonId,
            (LocalCityGmlObjectProjection.ParsedSurfaceSemantic)surface.Semantic,
            ToProjectionModel(surface.ExteriorRing),
            surface.InteriorRings.Select(ToProjectionModel).ToArray(),
            surface.BaseColor,
            surface.TexturePayload,
            surface.UsesGeneratedDemTexture,
            surface.OpticalProperties);
    }

    internal static ParsedSurface FromProjectionModel(LocalCityGmlObjectProjection.ParsedSurface surface)
    {
        return new ParsedSurface(
            surface.PolygonId,
            (ParsedSurfaceSemantic)surface.Semantic,
            FromProjectionModel(surface.ExteriorRing),
            surface.InteriorRings.Select(FromProjectionModel).ToArray(),
            new ColorRgba(surface.BaseColor.R, surface.BaseColor.G, surface.BaseColor.B, surface.BaseColor.A),
            surface.TexturePayload,
            surface.UsesGeneratedDemTexture,
            surface.OpticalProperties);
    }

    internal static LocalCityGmlObjectProjection.ParsedCityObject ToProjectionModel(ParsedCityObject cityObject)
    {
        return new LocalCityGmlObjectProjection.ParsedCityObject(
            cityObject.SlotKey,
            cityObject.DisplayName,
            cityObject.PackageName,
            cityObject.ActualMeshCode,
            cityObject.LodLevel,
            cityObject.Surfaces.Select(ToProjectionModel).ToArray(),
            cityObject.ReferenceSystem.ToProjectionModel(),
            cityObject.SourceFileRelativePath,
            cityObject.SharedAcrossMeshCodes,
            cityObject.TerrainAligned,
            cityObject.GeodeticOriginOverride?.ToProjectionModel(),
            cityObject.FloorsAboveGround,
            cityObject.MeasuredHeightMeters,
            cityObject.BuildingAttributes,
            cityObject.GeometryHeightMeters);
    }

    internal static ParsedCityObject FromProjectionModel(LocalCityGmlObjectProjection.ParsedCityObject cityObject)
    {
        return new ParsedCityObject(
            cityObject.SlotKey,
            cityObject.DisplayName,
            cityObject.PackageName,
            cityObject.ActualMeshCode,
            cityObject.LodLevel,
            cityObject.Surfaces.Select(FromProjectionModel).ToArray(),
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
