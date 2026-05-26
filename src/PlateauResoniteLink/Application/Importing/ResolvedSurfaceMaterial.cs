namespace PlateauResoniteLink.Application.Importing;

internal sealed record ResolvedSurfaceMaterial(
    ParsedSurface Surface,
    ResolvedMaterial Material,
    MaterialDepthOffset? DepthOffset);
