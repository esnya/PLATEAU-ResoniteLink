namespace PlateauResoniteLink.Application.Importing;

internal readonly record struct FacadeUvProjectionContext(
    double MinimumY,
    double MaximumY,
    double FloorHeightMeters,
    int FloorCount);
