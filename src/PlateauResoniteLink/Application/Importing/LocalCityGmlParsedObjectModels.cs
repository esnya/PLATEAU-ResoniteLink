using System.Collections.Generic;
using System.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record ParsedRing(
    string RingId,
    GeodeticPoint[] Vertices,
    IReadOnlyList<Float2>? UVs);

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
    double? GeometryHeightMeters = null);

internal sealed record ResolvedSurfaceMaterial(
    ParsedSurface Surface,
    ResolvedMaterial Material,
    MaterialDepthOffset? DepthOffset);
