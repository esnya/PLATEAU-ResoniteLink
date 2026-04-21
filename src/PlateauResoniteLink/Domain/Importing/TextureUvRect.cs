using System;

namespace PlateauResoniteLink.Domain.Importing;

internal readonly record struct TextureUvRect(
    double MinU,
    double MinV,
    double Width,
    double Height)
{
    public static TextureUvRect Identity { get; } = new(0.0, 0.0, 1.0, 1.0);

    public double MaxU => MinU + Width;

    public double MaxV => MinV + Height;

    public ResoniteFloat2 Scale => new(Width, Height);

    public ResoniteFloat2 Offset => new(MinU, MinV);

    public bool IsIdentity =>
        Math.Abs(MinU) < 1e-9
        && Math.Abs(MinV) < 1e-9
        && Math.Abs(Width - 1.0) < 1e-9
        && Math.Abs(Height - 1.0) < 1e-9;

    public static TextureUvRect FromScaleOffset(
        ResoniteFloat2 scale,
        ResoniteFloat2 offset)
    {
        ArgumentNullException.ThrowIfNull(scale);
        ArgumentNullException.ThrowIfNull(offset);

        return new TextureUvRect(offset.X, offset.Y, scale.X, scale.Y);
    }

    public static TextureUvRect FromTopLeftPixelRect(
        int x,
        int y,
        int width,
        int height,
        int canvasWidth,
        int canvasHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasHeight);

        return new TextureUvRect(
            (double)x / canvasWidth,
            (double)(canvasHeight - (y + height)) / canvasHeight,
            (double)width / canvasWidth,
            (double)height / canvasHeight);
    }

    public static ResoniteFloat2 Remap(
        ResoniteFloat2 sourceUv,
        TextureUvRect sourceRect,
        TextureUvRect targetRect)
    {
        ArgumentNullException.ThrowIfNull(sourceUv);

        return targetRect.Denormalize(
            sourceRect.NormalizeU(sourceUv.X),
            sourceRect.NormalizeV(sourceUv.Y));
    }

    public static (ResoniteFloat2? TextureScale, ResoniteFloat2? TextureOffset) ComposeMaterialTransform(
        TextureUvRect targetRect,
        ResoniteFloat2? textureScale,
        ResoniteFloat2? textureOffset)
    {
        ResoniteFloat2 effectiveScale = new(
            (textureScale?.X ?? 1.0) * targetRect.Width,
            (textureScale?.Y ?? 1.0) * targetRect.Height);
        ResoniteFloat2 effectiveOffset = new(
            ((textureOffset?.X) ?? 0.0) * targetRect.Width + targetRect.MinU,
            ((textureOffset?.Y) ?? 0.0) * targetRect.Height + targetRect.MinV);

        return (
            IsIdentityScale(effectiveScale) ? null : effectiveScale,
            IsZeroOffset(effectiveOffset) ? null : effectiveOffset);
    }

    public ResoniteFloat2 Denormalize(double normalizedU, double normalizedV)
    {
        return new ResoniteFloat2(
            MinU + (Math.Clamp(normalizedU, 0.0, 1.0) * Width),
            MinV + (Math.Clamp(normalizedV, 0.0, 1.0) * Height));
    }

    public double NormalizeU(double value)
    {
        return NormalizeAxis(value, MinU, Width);
    }

    public double NormalizeV(double value)
    {
        return NormalizeAxis(value, MinV, Height);
    }

    private static double NormalizeAxis(double value, double min, double length)
    {
        if (length <= 0.0)
        {
            return 0.0;
        }

        return Math.Clamp((value - min) / length, 0.0, 1.0);
    }

    private static bool IsIdentityScale(ResoniteFloat2 textureScale)
    {
        return Math.Abs(textureScale.X - 1.0) < 1e-9
            && Math.Abs(textureScale.Y - 1.0) < 1e-9;
    }

    private static bool IsZeroOffset(ResoniteFloat2 textureOffset)
    {
        return Math.Abs(textureOffset.X) < 1e-9
            && Math.Abs(textureOffset.Y) < 1e-9;
    }
}
