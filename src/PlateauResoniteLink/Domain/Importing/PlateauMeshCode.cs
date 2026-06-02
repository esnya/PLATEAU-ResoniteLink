namespace PlateauResoniteLink.Domain.Importing;

public static class PlateauMeshCode
{
    public static bool TryGetGeodeticCenter(string meshCode, out GeodeticCoordinate center)
    {
        if (SecondRegionalMeshCode.TryParse(meshCode, out SecondRegionalMeshCode? secondMeshCode))
        {
            center = secondMeshCode.Center;
            return true;
        }

        if (ThirdRegionalMeshCode.TryParse(meshCode, out ThirdRegionalMeshCode? thirdMeshCode))
        {
            center = thirdMeshCode.Center;
            return true;
        }

        center = new GeodeticCoordinate(0.0, 0.0, 0.0);
        return false;
    }

    public static bool TryGetBounds(
        string meshCode,
        out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds)
    {
        if (SecondRegionalMeshCode.TryParse(meshCode, out SecondRegionalMeshCode? secondMeshCode))
        {
            bounds = ToTuple(secondMeshCode.Bounds);
            return true;
        }

        if (ThirdRegionalMeshCode.TryParse(meshCode, out ThirdRegionalMeshCode? thirdMeshCode))
        {
            bounds = ToTuple(thirdMeshCode.Bounds);
            return true;
        }

        bounds = default;
        return false;
    }

    private static (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) ToTuple(
        JisRegionalMeshBounds bounds)
    {
        return (
            SouthLatitude: bounds.SouthLatitude,
            NorthLatitude: bounds.NorthLatitude,
            WestLongitude: bounds.WestLongitude,
            EastLongitude: bounds.EastLongitude);
    }
}
