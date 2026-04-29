using System.Collections.Generic;
using System.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record ParsedRing(
    string RingId,
    GeodeticPoint[] Vertices,
    IReadOnlyList<Float2>? UVs)
{
    internal LocalCityGmlObjectProjection.ParsedRing ToLegacy()
    {
        return new LocalCityGmlObjectProjection.ParsedRing(
            RingId,
            Vertices.Select(static point => point.ToLegacy()).ToArray(),
            UVs);
    }

    internal static ParsedRing FromLegacy(LocalCityGmlObjectProjection.ParsedRing ring)
    {
        return new ParsedRing(
            ring.RingId,
            ring.Vertices.Select(GeodeticPoint.FromLegacy).ToArray(),
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
    bool UsesGeneratedDemTexture = false)
{
    public IEnumerable<GeodeticPoint> Vertices =>
        ExteriorRing.Vertices.Concat(InteriorRings.SelectMany(static ring => ring.Vertices));

    internal LocalCityGmlObjectProjection.ParsedSurface ToLegacy()
    {
        return new LocalCityGmlObjectProjection.ParsedSurface(
            PolygonId,
            (LocalCityGmlObjectProjection.ParsedSurfaceSemantic)Semantic,
            ExteriorRing.ToLegacy(),
            InteriorRings.Select(static ring => ring.ToLegacy()).ToArray(),
            BaseColor,
            TexturePayload,
            UsesGeneratedDemTexture);
    }

    internal static ParsedSurface FromLegacy(LocalCityGmlObjectProjection.ParsedSurface surface)
    {
        return new ParsedSurface(
            surface.PolygonId,
            (ParsedSurfaceSemantic)surface.Semantic,
            ParsedRing.FromLegacy(surface.ExteriorRing),
            surface.InteriorRings.Select(ParsedRing.FromLegacy).ToArray(),
            new ColorRgba(surface.BaseColor.R, surface.BaseColor.G, surface.BaseColor.B, surface.BaseColor.A),
            surface.TexturePayload,
            surface.UsesGeneratedDemTexture);
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
    double? MeasuredHeightMeters = null)
{
    internal LocalCityGmlObjectProjection.ParsedCityObject ToLegacy()
    {
        return new LocalCityGmlObjectProjection.ParsedCityObject(
            SlotKey,
            DisplayName,
            PackageName,
            ActualMeshCode,
            LodLevel,
            Surfaces.Select(static surface => surface.ToLegacy()).ToArray(),
            ReferenceSystem.ToLegacy(),
            SourceFileRelativePath,
            SharedAcrossMeshCodes,
            TerrainAligned,
            GeodeticOriginOverride?.ToLegacy(),
            FloorsAboveGround,
            MeasuredHeightMeters);
    }

    internal static ParsedCityObject FromLegacy(LocalCityGmlObjectProjection.ParsedCityObject cityObject)
    {
        return new ParsedCityObject(
            cityObject.SlotKey,
            cityObject.DisplayName,
            cityObject.PackageName,
            cityObject.ActualMeshCode,
            cityObject.LodLevel,
            cityObject.Surfaces.Select(ParsedSurface.FromLegacy).ToArray(),
            CoordinateReferenceSystem.FromLegacy(cityObject.ReferenceSystem),
            cityObject.SourceFileRelativePath,
            cityObject.SharedAcrossMeshCodes,
            cityObject.TerrainAligned,
            cityObject.GeodeticOriginOverride is null ? null : GeodeticPoint.FromLegacy(cityObject.GeodeticOriginOverride),
            cityObject.FloorsAboveGround,
            cityObject.MeasuredHeightMeters);
    }
}
