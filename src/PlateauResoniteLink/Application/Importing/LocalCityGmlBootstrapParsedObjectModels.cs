using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record BootstrapParsedRing(
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

    internal static BootstrapParsedRing FromLegacy(LocalCityGmlObjectProjection.ParsedRing ring)
    {
        return new BootstrapParsedRing(
            ring.RingId,
            ring.Vertices.Select(GeodeticPoint.FromLegacy).ToArray(),
            ring.UVs);
    }
}

internal enum BootstrapParsedSurfaceSemantic
{
    Unknown = 0,
    Wall = 1,
    Roof = 2,
    Ground = 3,
    Closure = 4,
    OuterCeiling = 5,
    OuterFloor = 6,
}

internal sealed record BootstrapParsedSurface(
    string PolygonId,
    BootstrapParsedSurfaceSemantic Semantic,
    BootstrapParsedRing ExteriorRing,
    BootstrapParsedRing[] InteriorRings,
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
            new ResoniteColor(BaseColor.R, BaseColor.G, BaseColor.B, BaseColor.A),
            TexturePayload,
            UsesGeneratedDemTexture);
    }

    internal static BootstrapParsedSurface FromLegacy(LocalCityGmlObjectProjection.ParsedSurface surface)
    {
        return new BootstrapParsedSurface(
            surface.PolygonId,
            (BootstrapParsedSurfaceSemantic)surface.Semantic,
            BootstrapParsedRing.FromLegacy(surface.ExteriorRing),
            surface.InteriorRings.Select(BootstrapParsedRing.FromLegacy).ToArray(),
            new ColorRgba(surface.BaseColor.R, surface.BaseColor.G, surface.BaseColor.B, surface.BaseColor.A),
            surface.TexturePayload,
            surface.UsesGeneratedDemTexture);
    }
}

internal sealed record BootstrapParsedCityObject(
    string SlotKey,
    string DisplayName,
    string PackageName,
    string ActualMeshCode,
    int? LodLevel,
    BootstrapParsedSurface[] Surfaces,
    CoordinateReferenceSystem ReferenceSystem,
    string SourceFileRelativePath,
    string SourceUnitIdentity,
    string SourceIdentity,
    bool SharedAcrossMeshCodes,
    bool TerrainAligned = false,
    GeodeticPoint? OriginOverride = null,
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
            SourceUnitIdentity,
            SourceIdentity,
            SharedAcrossMeshCodes,
            TerrainAligned,
            OriginOverride?.ToLegacy(),
            FloorsAboveGround,
            MeasuredHeightMeters);
    }

    internal static BootstrapParsedCityObject FromLegacy(LocalCityGmlObjectProjection.ParsedCityObject cityObject)
    {
        return new BootstrapParsedCityObject(
            cityObject.SlotKey,
            cityObject.DisplayName,
            cityObject.PackageName,
            cityObject.ActualMeshCode,
            cityObject.LodLevel,
            cityObject.Surfaces.Select(BootstrapParsedSurface.FromLegacy).ToArray(),
            CoordinateReferenceSystem.FromLegacy(cityObject.ReferenceSystem),
            cityObject.SourceFileRelativePath,
            cityObject.SourceUnitIdentity,
            cityObject.SourceIdentity,
            cityObject.SharedAcrossMeshCodes,
            cityObject.TerrainAligned,
            cityObject.OriginOverride is null ? null : GeodeticPoint.FromLegacy(cityObject.OriginOverride),
            cityObject.FloorsAboveGround,
            cityObject.MeasuredHeightMeters);
    }
}
