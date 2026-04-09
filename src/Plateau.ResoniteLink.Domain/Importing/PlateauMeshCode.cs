using System.Globalization;

namespace Plateau.ResoniteLink.Domain.Importing;

public static class PlateauMeshCode
{
    public static bool TryGetCenter(string meshCode, out ResoniteLocalOrigin center)
    {
        center = default!;

        if (!TryGetBounds(meshCode, out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds))
        {
            return false;
        }

        center = new ResoniteLocalOrigin(
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

        if (string.IsNullOrWhiteSpace(meshCode)
            || (meshCode.Length != 6 && meshCode.Length != 8)
            || !meshCode.All(char.IsDigit))
        {
            return false;
        }

        int firstLatitudeIndex = int.Parse(meshCode[..2], CultureInfo.InvariantCulture);
        int firstLongitudeIndex = int.Parse(meshCode[2..4], CultureInfo.InvariantCulture);

        double southLatitude = firstLatitudeIndex / 1.5;
        double westLongitude = 100.0 + firstLongitudeIndex;
        double latitudeSpan = 40.0 / 60.0;
        double longitudeSpan = 1.0;

        if (meshCode.Length >= 6)
        {
            int secondLatitudeIndex = int.Parse(meshCode[4].ToString(), CultureInfo.InvariantCulture);
            int secondLongitudeIndex = int.Parse(meshCode[5].ToString(), CultureInfo.InvariantCulture);
            if (secondLatitudeIndex > 7 || secondLongitudeIndex > 7)
            {
                return false;
            }

            latitudeSpan /= 8.0;
            longitudeSpan /= 8.0;
            southLatitude += secondLatitudeIndex * latitudeSpan;
            westLongitude += secondLongitudeIndex * longitudeSpan;
        }

        if (meshCode.Length >= 8)
        {
            int thirdLatitudeIndex = int.Parse(meshCode[6].ToString(), CultureInfo.InvariantCulture);
            int thirdLongitudeIndex = int.Parse(meshCode[7].ToString(), CultureInfo.InvariantCulture);
            latitudeSpan /= 10.0;
            longitudeSpan /= 10.0;
            southLatitude += thirdLatitudeIndex * latitudeSpan;
            westLongitude += thirdLongitudeIndex * longitudeSpan;
        }

        bounds = (
            SouthLatitude: southLatitude,
            NorthLatitude: southLatitude + latitudeSpan,
            WestLongitude: westLongitude,
            EastLongitude: westLongitude + longitudeSpan);
        return true;
    }
}
