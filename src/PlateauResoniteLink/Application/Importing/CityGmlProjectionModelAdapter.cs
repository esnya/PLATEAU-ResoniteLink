using System.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlProjectionModelAdapter
{
    internal static ParsedRing FromProjectionModel(LocalCityGmlObjectProjection.ParsedRing ring)
    {
        return new ParsedRing(
            ring.RingId,
            ring.Vertices.Select(GeodeticPoint.FromProjectionModel).ToArray(),
            ring.UVs);
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

    internal static CachedSourceFileDescriptor FromProjectionModel(LocalCityGmlObjectProjection.CachedSourceFileDescriptor sourceFile)
    {
        return new CachedSourceFileDescriptor(
            SourceFileDescriptor.FromProjectionModel(sourceFile.SourceFile),
            sourceFile.CityObjects.Select(FromProjectionModel).ToArray());
    }

    internal static ParsedSourceFileResult FromProjectionModel(LocalCityGmlObjectProjection.ParsedSourceFileResult sourceFile)
    {
        return new ParsedSourceFileResult(
            SourceFileDescriptor.FromProjectionModel(sourceFile.SourceFile),
            sourceFile.CityObjects.Select(FromProjectionModel).ToArray(),
            sourceFile.ReferenceSystem is null ? null : CoordinateReferenceSystem.FromProjectionModel(sourceFile.ReferenceSystem),
            sourceFile.TerrainTriangles.Select(TerrainHeightTriangle.FromProjectionModel).ToArray(),
            sourceFile.Elapsed);
    }
}
