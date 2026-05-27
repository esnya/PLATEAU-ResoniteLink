using System;

namespace PlateauResoniteLink.Application.Importing;

internal static class GeneratedLod1RoofSurfaceIdentity
{
    internal static bool IsGenerated(ParsedSurface surface)
    {
        return surface.PolygonId.Contains("_generated_shed-", StringComparison.Ordinal)
            || surface.PolygonId.Contains("_generated_gable-", StringComparison.Ordinal)
            || surface.PolygonId.Contains("_generated_hip-", StringComparison.Ordinal)
            || surface.PolygonId.Contains("_generated_no-wall-", StringComparison.Ordinal);
    }

    internal static bool IsGeneratedNoWallSlabPart(ParsedSurface surface)
    {
        return surface.PolygonId.Contains("_generated_no-wall-", StringComparison.Ordinal);
    }
}
