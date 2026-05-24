namespace PlateauResoniteLink.Application.Importing;

internal static class DefaultMaterialSurfaceRoleMapper
{
    public static DefaultMaterialSurfaceRole From(ParsedSurfaceSemantic semantic)
    {
        return semantic switch
        {
            ParsedSurfaceSemantic.Wall => DefaultMaterialSurfaceRole.Wall,
            ParsedSurfaceSemantic.Roof => DefaultMaterialSurfaceRole.Roof,
            ParsedSurfaceSemantic.Ground => DefaultMaterialSurfaceRole.Ground,
            ParsedSurfaceSemantic.Closure => DefaultMaterialSurfaceRole.Closure,
            ParsedSurfaceSemantic.OuterCeiling => DefaultMaterialSurfaceRole.OuterCeiling,
            ParsedSurfaceSemantic.OuterFloor => DefaultMaterialSurfaceRole.OuterFloor,
            _ => DefaultMaterialSurfaceRole.Unknown,
        };
    }

}
