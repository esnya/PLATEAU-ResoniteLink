using System;
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

    public JisRegionalMeshBounds Bounds => JisRegionalMeshCodeCalculator.GetBounds(this);

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

    public JisRegionalMeshBounds Bounds => JisRegionalMeshCodeCalculator.GetBounds(this);

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

    public JisRegionalMeshBounds Bounds => JisRegionalMeshCodeCalculator.GetBounds(this);

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
    private static readonly JisRegionalMeshBounds EmptyBounds = new(0.0, 0.0, 0.0, 0.0);

    internal static bool IsValid(string? meshCode, int length)
    {
        return meshCode is not null
            && meshCode.Length == length
            && meshCode.All(static character => character is >= '0' and <= '9')
            && (length < 6 || (ToDigit(meshCode[4]) <= 7 && ToDigit(meshCode[5]) <= 7));
    }

    internal static JisRegionalMeshBounds GetBounds(FirstRegionalMeshCode meshCode)
    {
        return CalculateBounds(meshCode.Value, out JisRegionalMeshBounds bounds)
            ? bounds
            : throw new InvalidOperationException("A valid first regional mesh code did not produce bounds.");
    }

    internal static JisRegionalMeshBounds GetBounds(SecondRegionalMeshCode meshCode)
    {
        return CalculateBounds(meshCode.Value, out JisRegionalMeshBounds bounds)
            ? bounds
            : throw new InvalidOperationException("A valid second regional mesh code did not produce bounds.");
    }

    internal static JisRegionalMeshBounds GetBounds(ThirdRegionalMeshCode meshCode)
    {
        return CalculateBounds(meshCode.Value, out JisRegionalMeshBounds bounds)
            ? bounds
            : throw new InvalidOperationException("A valid third regional mesh code did not produce bounds.");
    }

    private static bool CalculateBounds(string meshCode, out JisRegionalMeshBounds bounds)
    {
        bounds = EmptyBounds;

        if ((meshCode.Length != 4 && meshCode.Length != 6 && meshCode.Length != 8)
            || !meshCode.All(static character => character is >= '0' and <= '9'))
        {
            return false;
        }

        int firstLatitudeIndex = ToTwoDigitNumber(meshCode[0], meshCode[1]);
        int firstLongitudeIndex = ToTwoDigitNumber(meshCode[2], meshCode[3]);

        double southLatitude = firstLatitudeIndex / 1.5;
        double westLongitude = 100.0 + firstLongitudeIndex;
        double latitudeSpan = 40.0 / 60.0;
        double longitudeSpan = 1.0;

        if (meshCode.Length >= 6)
        {
            int secondLatitudeIndex = ToDigit(meshCode[4]);
            int secondLongitudeIndex = ToDigit(meshCode[5]);
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
            int thirdLatitudeIndex = ToDigit(meshCode[6]);
            int thirdLongitudeIndex = ToDigit(meshCode[7]);
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

    private static int ToTwoDigitNumber(char tens, char ones)
    {
        return (ToDigit(tens) * 10) + ToDigit(ones);
    }

    private static int ToDigit(char character)
    {
        return character - '0';
    }
}
