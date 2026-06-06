namespace PlateauResoniteLink.Application.Importing;

internal sealed record ResolvedSurfaceMaterial(
    ConstructionFace Face,
    ResolvedMaterial Material,
    MaterialDepthOffset? DepthOffset,
    int Order = 0)
{
    public ResolvedSurfaceMaterial(
        ParsedSurface surface,
        ResolvedMaterial material,
        MaterialDepthOffset? depthOffset)
        : this(new ConstructionFace(surface, ConstructionCityObjectDraft.ResolveRole(surface)), material, depthOffset)
    {
    }

    public ParsedSurface Surface => Face.Surface;

    public ConstructionFaceRole Role => Face.Role;
}
