namespace PlateauResoniteLink.Application.Importing;

internal sealed record ResolvedSurfaceMaterial(
    ConstructionFace Face,
    ResolvedMaterial Material,
    MaterialDepthOffset? DepthOffset)
{
    public ResolvedSurfaceMaterial(
        ParsedSurface surface,
        ResolvedMaterial material,
        MaterialDepthOffset? DepthOffset)
        : this(new ConstructionFace(surface, ConstructionCityObjectDraft.ResolveRole(surface)), material, DepthOffset)
    {
    }

    public ParsedSurface Surface => Face.Surface;

    public ConstructionFaceRole Role => Face.Role;
}
