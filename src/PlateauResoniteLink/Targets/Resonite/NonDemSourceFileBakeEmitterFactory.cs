namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class NonDemSourceFileBakeEmitterFactory(
    ResoniteTextureImageLoader textureImageLoader,
    NonDemAtlasBakeBudget atlasBudget)
{
    public INonDemSourceFileBakeEmitter Create()
    {
        NonDemAtlasLayoutFactory layoutFactory = new(
            atlasBudget.EffectiveMaxAtlasSize,
            atlasBudget.TilePaddingPixels);
        return new NonDemSourceFileBakeEmitter(
            new NonDemCityObjectBakeCandidateFactory(textureImageLoader, atlasBudget.EffectiveMaxAtlasTextureEdge),
            new NonDemCityObjectBakeAssembler(
                layoutFactory,
                new NonDemAtlasImageRenderer(atlasBudget.TilePaddingPixels)),
            new NonDemAtlasBatchFitPolicy(layoutFactory));
    }
}
