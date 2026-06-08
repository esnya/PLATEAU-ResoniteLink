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

internal enum SurfaceMaterialTreatment
{
    Default = 0,
    RoadMarking,
    TerrainOverlayMaterialSource,
}

// Project-stage geometry role for material and tessellation routing. This is not the input CityGML surface type.
internal sealed record ConstructionFace(
    ParsedSurface Surface,
    ConstructionFaceRole Role,
    SurfaceMaterialTreatment MaterialTreatment = SurfaceMaterialTreatment.Default);

internal sealed record ConstructionCityObjectDraft
{
    public ConstructionCityObjectDraft(
        ParsedCityObject source,
        ConstructionFace[] faces,
        ConstructionFace[]? facadeUvReferenceFaces = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(faces);

        Faces = faces;
        Surfaces = faces.Select(static face => face.Surface).ToArray();
        Source = SameSurfaceReferences(source.Surfaces, Surfaces)
            ? source
            : source with { Surfaces = Surfaces };
        FacadeUvReferenceFaces = facadeUvReferenceFaces ?? faces;
    }

    public ParsedCityObject Source { get; }

    public ConstructionFace[] Faces { get; }

    public ParsedSurface[] Surfaces { get; }

    public ConstructionFace[] FacadeUvReferenceFaces { get; }

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
        return new ConstructionCityObjectDraft(cityObject, faces);
    }

    private static bool SameSurfaceReferences(ParsedSurface[] left, ParsedSurface[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Length; index++)
        {
            if (!ReferenceEquals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    internal static ConstructionFaceRole ResolveRole(ParsedSurface surface)
    {
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
