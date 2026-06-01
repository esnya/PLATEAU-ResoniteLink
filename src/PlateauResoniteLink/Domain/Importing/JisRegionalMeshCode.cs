using System;
using System.Globalization;
using System.Linq;

namespace PlateauResoniteLink.Domain.Importing;

public sealed record JisRegionalMeshBounds(
    double SouthLatitude,
    double NorthLatitude,
    double WestLongitude,
    double EastLongitude);

public readonly record struct FirstRegionalMeshCode
{
    private FirstRegionalMeshCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public JisRegionalMeshBounds Bounds => JisRegionalMeshCodeCalculator.GetBounds(Value);

    public static bool TryParse(string? value, out FirstRegionalMeshCode meshCode)
    {
        if (JisRegionalMeshCodeCalculator.IsValid(value, 4))
        {
            meshCode = new FirstRegionalMeshCode(value!);
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
}

public readonly record struct SecondRegionalMeshCode
{
    private SecondRegionalMeshCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public JisRegionalMeshBounds Bounds => JisRegionalMeshCodeCalculator.GetBounds(Value);

    public FirstRegionalMeshCode Parent => FirstRegionalMeshCode.Parse(Value[..4]);

    public static bool TryParse(string? value, out SecondRegionalMeshCode meshCode)
    {
        if (JisRegionalMeshCodeCalculator.IsValid(value, 6))
        {
            meshCode = new SecondRegionalMeshCode(value!);
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
}

public readonly record struct ThirdRegionalMeshCode
{
    private ThirdRegionalMeshCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public JisRegionalMeshBounds Bounds => JisRegionalMeshCodeCalculator.GetBounds(Value);

    public SecondRegionalMeshCode Parent => SecondRegionalMeshCode.Parse(Value[..6]);

    public FirstRegionalMeshCode FirstMesh => FirstRegionalMeshCode.Parse(Value[..4]);

    public static bool TryParse(string? value, out ThirdRegionalMeshCode meshCode)
    {
        if (JisRegionalMeshCodeCalculator.IsValid(value, 8))
        {
            meshCode = new ThirdRegionalMeshCode(value!);
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
}

internal static class JisRegionalMeshCodeCalculator
{
    internal static bool IsValid(string? meshCode, int length)
    {
        return meshCode is not null
            && meshCode.Length == length
            && meshCode.All(static character => character is >= '0' and <= '9')
            && TryGetBounds(meshCode, out _);
    }

    internal static JisRegionalMeshBounds GetBounds(string meshCode)
    {
        return TryGetBounds(meshCode, out JisRegionalMeshBounds bounds)
            ? bounds
            : throw new ArgumentException("JIS X 0410 regional mesh code is invalid.", nameof(meshCode));
    }

    internal static bool TryGetBounds(string? meshCode, out JisRegionalMeshBounds bounds)
    {
        bounds = new JisRegionalMeshBounds(0.0, 0.0, 0.0, 0.0);

        if (string.IsNullOrWhiteSpace(meshCode)
            || (meshCode.Length != 4 && meshCode.Length != 6 && meshCode.Length != 8)
            || !meshCode.All(static character => character is >= '0' and <= '9'))
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
            if (thirdLatitudeIndex > 9 || thirdLongitudeIndex > 9)
            {
                return false;
            }

            latitudeSpan /= 10.0;
            longitudeSpan /= 10.0;
            southLatitude += thirdLatitudeIndex * latitudeSpan;
            westLongitude += thirdLongitudeIndex * longitudeSpan;
        }

        bounds = new JisRegionalMeshBounds(
            southLatitude,
            southLatitude + latitudeSpan,
            westLongitude,
            westLongitude + longitudeSpan);
        return true;
    }
}
