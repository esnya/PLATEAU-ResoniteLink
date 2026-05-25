namespace PlateauResoniteLink.Application.Importing;

internal sealed record ResolvedSurfaceMaterial(
    LocalCityGmlObjectProjection.ParsedSurface Surface,
    ResolvedMaterial Material,
    MaterialDepthOffset? DepthOffset);
