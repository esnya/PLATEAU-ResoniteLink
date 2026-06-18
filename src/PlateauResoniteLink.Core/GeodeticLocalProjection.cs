using GeographicLib;

using PlateauResoniteLink.Core.Domain.Importing;

namespace PlateauResoniteLink.Core;

public readonly record struct LocalCartesianOffset(
    double EastMeters,
    double NorthMeters,
    double UpMeters);

public static class GeodeticLocalProjection
{
    public static LocalCartesianOffset Project(
        GeodeticCoordinate origin,
        GeodeticCoordinate coordinate)
    {
        LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            Geocentric.WGS84);
        (double east, double north, double up) = cartesian.Forward(
            coordinate.Latitude,
            coordinate.Longitude,
            coordinate.Altitude);
        return new LocalCartesianOffset(east, north, up);
    }

    public static GeodeticCoordinate Reverse(
        GeodeticCoordinate origin,
        double eastMeters,
        double northMeters,
        double upMeters = 0.0)
    {
        LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            Geocentric.WGS84);
        (double latitude, double longitude, double altitude) = cartesian.Reverse(
            eastMeters,
            northMeters,
            upMeters);
        return new GeodeticCoordinate(latitude, longitude, altitude);
    }
}
