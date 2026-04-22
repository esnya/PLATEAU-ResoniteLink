namespace PlateauResoniteLink.Domain.Importing;

public sealed record ResoniteLocalOrigin(
    double Latitude,
    double Longitude,
    double Altitude) : GeodeticCoordinate(Latitude, Longitude, Altitude);
