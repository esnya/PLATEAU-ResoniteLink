using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class DefaultMaterialSurfaceRoleMapperTests
{
    [Theory]
    [InlineData((int)ParsedSurfaceSemantic.Wall, (int)DefaultMaterialSurfaceRole.Wall)]
    [InlineData((int)ParsedSurfaceSemantic.Roof, (int)DefaultMaterialSurfaceRole.Roof)]
    [InlineData((int)ParsedSurfaceSemantic.Ground, (int)DefaultMaterialSurfaceRole.Ground)]
    [InlineData((int)ParsedSurfaceSemantic.Closure, (int)DefaultMaterialSurfaceRole.Closure)]
    [InlineData((int)ParsedSurfaceSemantic.OuterCeiling, (int)DefaultMaterialSurfaceRole.OuterCeiling)]
    [InlineData((int)ParsedSurfaceSemantic.OuterFloor, (int)DefaultMaterialSurfaceRole.OuterFloor)]
    [InlineData((int)ParsedSurfaceSemantic.Unknown, (int)DefaultMaterialSurfaceRole.Unknown)]
    public void FromMapsParsedSurfaceSemanticToDefaultMaterialRole(int semanticValue, int expectedRoleValue)
    {
        ParsedSurfaceSemantic semantic = (ParsedSurfaceSemantic)semanticValue;
        DefaultMaterialSurfaceRole expectedRole = (DefaultMaterialSurfaceRole)expectedRoleValue;

        Assert.Equal(expectedRole, DefaultMaterialSurfaceRoleMapper.From(semantic));
    }

    [Theory]
    [InlineData((int)ParsedSurfaceSemantic.Wall, (int)DefaultMaterialSurfaceRole.Wall)]
    [InlineData((int)ParsedSurfaceSemantic.Roof, (int)DefaultMaterialSurfaceRole.Roof)]
    [InlineData((int)ParsedSurfaceSemantic.Ground, (int)DefaultMaterialSurfaceRole.Ground)]
    [InlineData((int)ParsedSurfaceSemantic.Closure, (int)DefaultMaterialSurfaceRole.Closure)]
    [InlineData((int)ParsedSurfaceSemantic.OuterCeiling, (int)DefaultMaterialSurfaceRole.OuterCeiling)]
    [InlineData((int)ParsedSurfaceSemantic.OuterFloor, (int)DefaultMaterialSurfaceRole.OuterFloor)]
    [InlineData((int)ParsedSurfaceSemantic.Unknown, (int)DefaultMaterialSurfaceRole.Unknown)]
    public void FromMapsProjectionSurfaceSemanticToDefaultMaterialRole(int semanticValue, int expectedRoleValue)
    {
        ParsedSurfaceSemantic semantic =
            (ParsedSurfaceSemantic)semanticValue;
        DefaultMaterialSurfaceRole expectedRole = (DefaultMaterialSurfaceRole)expectedRoleValue;

        Assert.Equal(expectedRole, DefaultMaterialSurfaceRoleMapper.From(semantic));
    }
}
