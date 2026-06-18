
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Domain;

public sealed class TextureUvRectTests
{
    [Fact]
    public void ScaleAndOffsetValueExposeCanonicalScalarPairs()
    {
        TextureUvRect rect = new(0.125, 0.25, 0.5, 0.75);

        Assert.Equal(new ScalarPair(0.5, 0.75), rect.ScaleValue);
        Assert.Equal(new ScalarPair(0.125, 0.25), rect.OffsetValue);
    }

    [Fact]
    public void ComposeMaterialTransformValueUsesCanonicalScalarPairs()
    {
        TextureUvRect rect = new(0.2, 0.3, 0.4, 0.5);

        (ScalarPair? scale, ScalarPair? offset) = TextureUvRect.ComposeMaterialTransformValue(
            rect,
            new ScalarPair(2.0, 3.0),
            new ScalarPair(0.25, 0.5));

        Assert.NotNull(scale);
        Assert.NotNull(offset);
        Assert.Equal(0.8, scale!.X, 9);
        Assert.Equal(1.5, scale.Y, 9);
        Assert.Equal(0.3, offset!.X, 9);
        Assert.Equal(0.55, offset.Y, 9);
    }

    [Fact]
    public void RemapValueAndDenormalizeValueUseCanonicalScalarPairs()
    {
        TextureUvRect source = new(0.25, 0.5, 0.5, 0.25);
        TextureUvRect target = new(0.1, 0.2, 0.8, 0.6);

        ScalarPair remapped = TextureUvRect.RemapValue(
            new ScalarPair(0.5, 0.625),
            source,
            target);
        ScalarPair denormalized = target.DenormalizeValue(0.5, 0.25);

        Assert.IsType<ScalarPair>(remapped);
        Assert.IsNotType<ResoniteFloat2>(remapped);
        Assert.Equal(0.5, remapped.X, 9);
        Assert.Equal(0.5, remapped.Y, 9);
        Assert.Equal(0.5, denormalized.X, 9);
        Assert.Equal(0.35, denormalized.Y, 9);
    }
}
