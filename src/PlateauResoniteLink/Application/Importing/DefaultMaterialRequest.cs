namespace PlateauResoniteLink.Application.Importing;

internal enum DefaultMaterialSurfaceRole
{
    Unknown = 0,
    Wall,
    Roof,
    Ground,
    Closure,
    OuterCeiling,
    OuterFloor,
}

internal sealed record DefaultMaterialRequest(
    string PackageName,
    TexturePayload? TexturePayload,
    bool PreferUvProjection,
    DefaultMaterialFamilyOverride? FamilyOverride,
    string VariantSelectionKey,
    BuildingAttributeContext BuildingAttributes,
    int? FloorsAboveGround = null,
    double? MeasuredHeightMeters = null,
    double? GeometryHeightMeters = null,
    double? FootprintAreaSquareMeters = null,
    DefaultMaterialSurfaceRole SurfaceRole = DefaultMaterialSurfaceRole.Unknown);
