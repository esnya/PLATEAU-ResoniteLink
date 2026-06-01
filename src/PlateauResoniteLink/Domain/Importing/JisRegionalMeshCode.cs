using System;
using System.Linq;

namespace PlateauResoniteLink.Domain.Importing;

public sealed record JisRegionalMeshBounds(
    double SouthLatitude,
    double NorthLatitude,
    double WestLongitude,
    double EastLongitude)
{
    public GeodeticCoordinate Center => new(
        Latitude: (SouthLatitude + NorthLatitude) / 2.0,
        Longitude: (WestLongitude + EastLongitude) / 2.0,
        Altitude: 0.0);
}

public readonly record struct FirstRegionalMeshCode
{
    private FirstRegionalMeshCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public JisRegionalMeshBounds Bounds => JisRegionalMeshCodeCalculator.GetBounds(this);

    public GeodeticCoordinate Center => Bounds.Center;

    public static bool TryParse(string? value, out FirstRegionalMeshCode meshCode)
    {
        if (value is not null && JisRegionalMeshCodeCalculator.IsValid(value, 4))
        {
            meshCode = FromValidated(value);
            return true;
        }

        meshCode = default;
        return false;
    }

    public static FirstRegionalMeshCode Parse(string value)
    {
        return TryParse(value, out FirstRegionalMeshCode meshCode)
            ? meshCode
            : throw new ArgumentException("JIS X 0410 first regional mesh code must be a valid 4-digit code.", nameof(value));
    }

    public override string ToString() => Value;

    internal static FirstRegionalMeshCode FromValidated(string value)
    {
        return new FirstRegionalMeshCode(value);
    }
}

public readonly record struct SecondRegionalMeshCode
{
    private SecondRegionalMeshCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public JisRegionalMeshBounds Bounds => JisRegionalMeshCodeCalculator.GetBounds(this);

    public GeodeticCoordinate Center => Bounds.Center;

    public FirstRegionalMeshCode Parent => FirstRegionalMeshCode.FromValidated(Value[..4]);

    public static bool TryParse(string? value, out SecondRegionalMeshCode meshCode)
    {
        if (value is not null && JisRegionalMeshCodeCalculator.IsValid(value, 6))
        {
            meshCode = FromValidated(value);
            return true;
        }

        meshCode = default;
        return false;
    }

    public static SecondRegionalMeshCode Parse(string value)
    {
        return TryParse(value, out SecondRegionalMeshCode meshCode)
            ? meshCode
            : throw new ArgumentException("JIS X 0410 second regional mesh code must be a valid 6-digit code.", nameof(value));
    }

    public override string ToString() => Value;

    internal static SecondRegionalMeshCode FromValidated(string value)
    {
        return new SecondRegionalMeshCode(value);
    }
}

public readonly record struct ThirdRegionalMeshCode
{
    private ThirdRegionalMeshCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public JisRegionalMeshBounds Bounds => JisRegionalMeshCodeCalculator.GetBounds(this);

    public GeodeticCoordinate Center => Bounds.Center;

    public SecondRegionalMeshCode Parent => SecondRegionalMeshCode.FromValidated(Value[..6]);

    public FirstRegionalMeshCode FirstMesh => FirstRegionalMeshCode.FromValidated(Value[..4]);

    public static bool TryParse(string? value, out ThirdRegionalMeshCode meshCode)
    {
        if (value is not null && JisRegionalMeshCodeCalculator.IsValid(value, 8))
        {
            meshCode = FromValidated(value);
            return true;
        }

        meshCode = default;
        return false;
    }

    public static ThirdRegionalMeshCode Parse(string value)
    {
        return TryParse(value, out ThirdRegionalMeshCode meshCode)
            ? meshCode
            : throw new ArgumentException("JIS X 0410 third regional mesh code must be a valid 8-digit code.", nameof(value));
    }

    public override string ToString() => Value;

    internal static ThirdRegionalMeshCode FromValidated(string value)
    {
        return new ThirdRegionalMeshCode(value);
    }
}

internal static class JisRegionalMeshCodeCalculator
{
    internal static bool IsValid(string? meshCode, int length)
    {
        return meshCode is not null
            && meshCode.Length == length
            && meshCode.All(static character => character is >= '0' and <= '9')
            && (length < 6 || (ToDigit(meshCode[4]) <= 7 && ToDigit(meshCode[5]) <= 7));
    }

    internal static JisRegionalMeshBounds GetBounds(FirstRegionalMeshCode meshCode)
    {
        return CalculateFirstBounds(meshCode.Value);
    }

    internal static JisRegionalMeshBounds GetBounds(SecondRegionalMeshCode meshCode)
    {
        return CalculateSecondBounds(meshCode.Value);
    }

    internal static JisRegionalMeshBounds GetBounds(ThirdRegionalMeshCode meshCode)
    {
        return CalculateThirdBounds(meshCode.Value);
    }

    private static JisRegionalMeshBounds CalculateFirstBounds(string meshCode)
    {
        int firstLatitudeIndex = ToTwoDigitNumber(meshCode[0], meshCode[1]);
        int firstLongitudeIndex = ToTwoDigitNumber(meshCode[2], meshCode[3]);

        double southLatitude = firstLatitudeIndex / 1.5;
        double westLongitude = 100.0 + firstLongitudeIndex;
        double latitudeSpan = 40.0 / 60.0;
        double longitudeSpan = 1.0;

        return new JisRegionalMeshBounds(
            southLatitude,
            southLatitude + latitudeSpan,
            westLongitude,
            westLongitude + longitudeSpan);
    }

    private static JisRegionalMeshBounds CalculateSecondBounds(string meshCode)
    {
        JisRegionalMeshBounds firstBounds = CalculateFirstBounds(meshCode);
        double latitudeSpan = (firstBounds.NorthLatitude - firstBounds.SouthLatitude) / 8.0;
        double longitudeSpan = (firstBounds.EastLongitude - firstBounds.WestLongitude) / 8.0;
        double southLatitude = firstBounds.SouthLatitude + (ToDigit(meshCode[4]) * latitudeSpan);
        double westLongitude = firstBounds.WestLongitude + (ToDigit(meshCode[5]) * longitudeSpan);

        return new JisRegionalMeshBounds(
            southLatitude,
            southLatitude + latitudeSpan,
            westLongitude,
            westLongitude + longitudeSpan);
    }

    private static JisRegionalMeshBounds CalculateThirdBounds(string meshCode)
    {
        JisRegionalMeshBounds secondBounds = CalculateSecondBounds(meshCode);
        double latitudeSpan = (secondBounds.NorthLatitude - secondBounds.SouthLatitude) / 10.0;
        double longitudeSpan = (secondBounds.EastLongitude - secondBounds.WestLongitude) / 10.0;
        double southLatitude = secondBounds.SouthLatitude + (ToDigit(meshCode[6]) * latitudeSpan);
        double westLongitude = secondBounds.WestLongitude + (ToDigit(meshCode[7]) * longitudeSpan);

        return new JisRegionalMeshBounds(
            southLatitude,
            southLatitude + latitudeSpan,
            westLongitude,
            westLongitude + longitudeSpan);
    }

    private static int ToTwoDigitNumber(char tens, char ones)
    {
        return (ToDigit(tens) * 10) + ToDigit(ones);
    }

    private static int ToDigit(char character)
    {
        return character - '0';
    }
}
