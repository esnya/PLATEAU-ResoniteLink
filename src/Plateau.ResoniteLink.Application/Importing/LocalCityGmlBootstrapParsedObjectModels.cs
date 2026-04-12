using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed record BootstrapParsedRing(
    string RingId,
    GeodeticPoint[] Vertices,
    IReadOnlyList<ResoniteFloat2>? UVs)
{
    internal LocalCityGmlResonitePlanBuilder.ParsedRing ToLegacy()
    {
        return new LocalCityGmlResonitePlanBuilder.ParsedRing(
            RingId,
            Vertices.Select(static point => point.ToLegacy()).ToArray(),
            UVs);
    }

    internal static BootstrapParsedRing FromLegacy(LocalCityGmlResonitePlanBuilder.ParsedRing ring)
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
    ResoniteColor BaseColor,
    string? TexturePath)
{
    public IEnumerable<GeodeticPoint> Vertices =>
        ExteriorRing.Vertices.Concat(InteriorRings.SelectMany(static ring => ring.Vertices));

    internal LocalCityGmlResonitePlanBuilder.ParsedSurface ToLegacy()
    {
        return new LocalCityGmlResonitePlanBuilder.ParsedSurface(
            PolygonId,
            (LocalCityGmlResonitePlanBuilder.ParsedSurfaceSemantic)Semantic,
            ExteriorRing.ToLegacy(),
            InteriorRings.Select(static ring => ring.ToLegacy()).ToArray(),
            BaseColor,
            TexturePath);
    }

    internal static BootstrapParsedSurface FromLegacy(LocalCityGmlResonitePlanBuilder.ParsedSurface surface)
    {
        return new BootstrapParsedSurface(
            surface.PolygonId,
            (BootstrapParsedSurfaceSemantic)surface.Semantic,
            BootstrapParsedRing.FromLegacy(surface.ExteriorRing),
            surface.InteriorRings.Select(BootstrapParsedRing.FromLegacy).ToArray(),
            surface.BaseColor,
            surface.TexturePath);
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
    string SourceUnitIdentity,
    string SourceIdentity,
    bool SharedAcrossMeshCodes,
    bool TerrainAligned = false,
    GeodeticPoint? OriginOverride = null)
{
    internal LocalCityGmlResonitePlanBuilder.ParsedCityObject ToLegacy()
    {
        return new LocalCityGmlResonitePlanBuilder.ParsedCityObject(
            SlotKey,
            DisplayName,
            PackageName,
            ActualMeshCode,
            LodLevel,
            Surfaces.Select(static surface => surface.ToLegacy()).ToArray(),
            ReferenceSystem.ToLegacy(),
            SourceUnitIdentity,
            SourceIdentity,
            SharedAcrossMeshCodes,
            TerrainAligned,
            OriginOverride?.ToLegacy());
    }

    internal static BootstrapParsedCityObject FromLegacy(LocalCityGmlResonitePlanBuilder.ParsedCityObject cityObject)
    {
        return new BootstrapParsedCityObject(
            cityObject.SlotKey,
            cityObject.DisplayName,
            cityObject.PackageName,
            cityObject.ActualMeshCode,
            cityObject.LodLevel,
            cityObject.Surfaces.Select(BootstrapParsedSurface.FromLegacy).ToArray(),
            CoordinateReferenceSystem.FromLegacy(cityObject.ReferenceSystem),
            cityObject.SourceUnitIdentity,
            cityObject.SourceIdentity,
            cityObject.SharedAcrossMeshCodes,
            cityObject.TerrainAligned,
            cityObject.OriginOverride is null ? null : GeodeticPoint.FromLegacy(cityObject.OriginOverride));
    }
}
