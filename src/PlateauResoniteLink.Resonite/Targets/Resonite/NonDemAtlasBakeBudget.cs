using System;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal readonly record struct NonDemAtlasBakeBudget(
    int MaxAtlasSize = NonDemAtlasBakeBudget.DefaultMaxAtlasSize,
    int TilePaddingPixels = NonDemAtlasBakeBudget.DefaultTilePaddingPixels,
    ResoniteImportBudgetProfile? ResourceBudget = null)
{
    public const int DefaultMaxAtlasSize = 4096;
    public const int DefaultTilePaddingPixels = 2;

    public int EffectiveMaxAtlasSize => Math.Max(1, Math.Min(MaxAtlasSize, ResourceBudget?.MaxAtlasSize ?? MaxAtlasSize));

    public int EffectiveMaxAtlasTextureEdge
    {
        get
        {
            int profileMaxTileEdge = ResourceBudget?.MaxAtlasTextureEdge ?? EffectiveMaxAtlasSize;
            return Math.Max(1, Math.Min(EffectiveMaxAtlasSize - (TilePaddingPixels * 2), profileMaxTileEdge));
        }
    }
}
