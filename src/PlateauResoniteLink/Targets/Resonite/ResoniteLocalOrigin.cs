using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed record ResoniteLocalOrigin(
    double Latitude,
    double Longitude,
    double Altitude) : GeodeticCoordinate(Latitude, Longitude, Altitude);
