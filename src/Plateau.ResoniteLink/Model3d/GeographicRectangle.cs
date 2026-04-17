namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record GeographicRectangle(
    double MinLatitude,
    double MaxLatitude,
    double MinLongitude,
    double MaxLongitude);
