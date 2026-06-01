using System.Diagnostics.CodeAnalysis;

namespace PlateauResoniteLink.Domain.Importing;

public abstract record PlateauRegionalMeshCode
{
    private PlateauRegionalMeshCode(string value, JisRegionalMeshBounds bounds)
    {
        Value = value;
        Bounds = bounds;
    }

    public string Value { get; }

    public JisRegionalMeshBounds Bounds { get; }

    public GeodeticCoordinate Center => new(
        Latitude: (Bounds.SouthLatitude + Bounds.NorthLatitude) / 2.0,
        Longitude: (Bounds.WestLongitude + Bounds.EastLongitude) / 2.0,
        Altitude: 0.0);

    public static bool TryParse(string? value, [NotNullWhen(true)] out PlateauRegionalMeshCode? meshCode)
    {
        if (SecondRegionalMeshCode.TryParse(value, out SecondRegionalMeshCode secondMeshCode))
        {
            meshCode = new Second(secondMeshCode);
            return true;
        }

        if (ThirdRegionalMeshCode.TryParse(value, out ThirdRegionalMeshCode thirdMeshCode))
        {
            meshCode = new Third(thirdMeshCode);
            return true;
        }

        meshCode = null;
        return false;
    }

    public sealed record Second(SecondRegionalMeshCode Code)
        : PlateauRegionalMeshCode(Code.Value, Code.Bounds);

    public sealed record Third(ThirdRegionalMeshCode Code)
        : PlateauRegionalMeshCode(Code.Value, Code.Bounds);

    public override string ToString() => Value;
}

public static class PlateauMeshCode
{
    public static bool TryGetGeodeticCenter(string meshCode, out GeodeticCoordinate center)
    {
        if (!PlateauRegionalMeshCode.TryParse(meshCode, out PlateauRegionalMeshCode? regionalMeshCode))
        {
            center = new GeodeticCoordinate(0.0, 0.0, 0.0);
            return false;
        }

        center = regionalMeshCode.Center;
        return true;
    }

    public static bool TryGetBounds(
        string meshCode,
        out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds)
    {
        bounds = default;

        if (!PlateauRegionalMeshCode.TryParse(meshCode, out PlateauRegionalMeshCode? regionalMeshCode))
        {
            return false;
        }

        JisRegionalMeshBounds jisBounds = regionalMeshCode.Bounds;
        bounds = (
            SouthLatitude: jisBounds.SouthLatitude,
            NorthLatitude: jisBounds.NorthLatitude,
            WestLongitude: jisBounds.WestLongitude,
            EastLongitude: jisBounds.EastLongitude);
        return true;
    }
}
