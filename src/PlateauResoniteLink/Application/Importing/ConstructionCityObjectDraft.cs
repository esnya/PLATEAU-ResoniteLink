using System;
using System.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal enum ConstructionFaceRole
{
    Unknown = 0,
    Wall,
    Roof,
    RoofSlab,
    Ground,
    Closure,
    OuterCeiling,
    OuterFloor,
}

// Project-stage geometry role for material and tessellation routing. This is not the input CityGML surface type.
internal sealed record ConstructionFace(
    ParsedSurface Surface,
    ConstructionFaceRole Role);

internal sealed record ConstructionCityObjectDraft(
    ParsedCityObject Source,
    ConstructionFace[] Faces,
    ParsedSurface[] Surfaces)
{
    public string SlotKey => Source.SlotKey;

    public string DisplayName => Source.DisplayName;

    public string PackageName => Source.PackageName;

    public string ActualMeshCode => Source.ActualMeshCode;

    public int? LodLevel => Source.LodLevel;

    public CoordinateReferenceSystem ReferenceSystem => Source.ReferenceSystem;

    public string SourceFileRelativePath => Source.SourceFileRelativePath;

    public bool TerrainAligned => Source.TerrainAligned;

    public GeodeticPoint? GeodeticOriginOverride => Source.GeodeticOriginOverride;

    public int? FloorsAboveGround => Source.FloorsAboveGround;

    public double? MeasuredHeightMeters => Source.MeasuredHeightMeters;

    public BuildingAttributeContext BuildingAttributes => Source.BuildingAttributes;

    public double? GeometryHeightMeters => Source.GeometryHeightMeters;

    public static ConstructionCityObjectDraft FromParsedCityObject(ParsedCityObject cityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        ConstructionFace[] faces = cityObject.Surfaces
            .Select(static surface => new ConstructionFace(surface, ResolveRole(surface)))
            .ToArray();
        return new ConstructionCityObjectDraft(
            cityObject,
            faces,
            cityObject.Surfaces);
    }

    internal static ConstructionFaceRole ResolveRole(ParsedSurface surface)
    {
        if (GeneratedLod1RoofSurfaceIdentity.IsGeneratedNoWallSlabPart(surface))
        {
            return ConstructionFaceRole.RoofSlab;
        }

        return surface.Semantic switch
        {
            ParsedSurfaceSemantic.Wall => ConstructionFaceRole.Wall,
            ParsedSurfaceSemantic.Roof => ConstructionFaceRole.Roof,
            ParsedSurfaceSemantic.Ground => ConstructionFaceRole.Ground,
            ParsedSurfaceSemantic.Closure => ConstructionFaceRole.Closure,
            ParsedSurfaceSemantic.OuterCeiling => ConstructionFaceRole.OuterCeiling,
            ParsedSurfaceSemantic.OuterFloor => ConstructionFaceRole.OuterFloor,
            _ => ConstructionFaceRole.Unknown,
        };
    }
}
