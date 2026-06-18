namespace PlateauResoniteLink.Core.Domain.Importing;

public static class PlateauMeshCode
{
    public static bool TryGetGeodeticCenter(string meshCode, out GeodeticCoordinate center)
    {
        center = default!;

        if (!TryGetBounds(meshCode, out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds))
        {
            return false;
        }

        center = new GeodeticCoordinate(
            Latitude: (bounds.SouthLatitude + bounds.NorthLatitude) / 2.0,
            Longitude: (bounds.WestLongitude + bounds.EastLongitude) / 2.0,
            Altitude: 0.0);
        return true;
    }

    public static bool TryGetBounds(
        string meshCode,
        out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds)
    {
        bounds = default;

        if (!JisRegionalMeshCodeCalculator.TryGetBounds(meshCode, out JisRegionalMeshBounds? jisBounds)
            || meshCode.Length == 4)
        {
            return false;
        }

        bounds = (
            SouthLatitude: jisBounds!.SouthLatitude,
            NorthLatitude: jisBounds.NorthLatitude,
            WestLongitude: jisBounds.WestLongitude,
            EastLongitude: jisBounds.EastLongitude);
        return true;
    }
}
