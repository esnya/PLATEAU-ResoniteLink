using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Core.Application.Importing.Contracts;
using PlateauResoniteLink.Plateau.Application.Importing.Plateau;

namespace PlateauResoniteLink.Plateau.Application.Importing.Source;

internal sealed record ParsedRing(
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
    ParsedSurfaceSemantic Semantic,
    ParsedRing ExteriorRing,
    ParsedRing[] InteriorRings,
    ColorRgba BaseColor,
    TexturePayload? TexturePayload,
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
    BuildingAttributeContext BuildingAttributes,
    bool TerrainAligned = false,
    GeodeticPoint? GeodeticOriginOverride = null,
    int? FloorsAboveGround = null,
    double? MeasuredHeightMeters = null,
    double? GeometryHeightMeters = null,
    string? SourceMeshCode = null);
