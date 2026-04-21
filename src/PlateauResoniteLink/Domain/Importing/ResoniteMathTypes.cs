namespace PlateauResoniteLink.Domain.Importing;

public sealed record ResoniteFloat2(
    double X,
    double Y);

public sealed record ResoniteFloat3(
    double X,
    double Y,
    double Z);

public sealed record ResoniteFloatQ(
    double X,
    double Y,
    double Z,
    double W);

public sealed record ResoniteColor(
    double R,
    double G,
    double B,
    double A);

public sealed record ResoniteTransform(
    ResoniteFloat3 Position,
    ResoniteFloatQ? Rotation = null);
