namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal interface INonDemSourceFileBakeEmitterFactory
{
    INonDemSourceFileBakeEmitter Create(
        NonDemAtlasBakeBudget atlasBudget,
        ResoniteLocalOrigin requestLocalOrigin);
}

internal sealed class NonDemSourceFileBakeEmitterFactory(
    ResoniteTextureImageLoader textureImageLoader) : INonDemSourceFileBakeEmitterFactory
{
    public INonDemSourceFileBakeEmitter Create(
        NonDemAtlasBakeBudget atlasBudget,
        ResoniteLocalOrigin requestLocalOrigin)
    {
        NonDemAtlasLayoutFactory layoutFactory = new(
            atlasBudget.EffectiveMaxAtlasSize,
            atlasBudget.TilePaddingPixels);
        return new NonDemSourceFileBakeEmitter(
            new NonDemCityObjectBakeCandidateFactory(
                new NonDemBakeEntryFactory(textureImageLoader, atlasBudget.EffectiveMaxAtlasTextureEdge)),
            new NonDemCityObjectBakeAssembler(
                layoutFactory,
                new NonDemAtlasImageRenderer(atlasBudget.TilePaddingPixels),
                new NonDemBakedGeometryComposer(requestLocalOrigin)),
            new NonDemAtlasBatchFitPolicy(layoutFactory),
            new NonDemBakeCandidateImageDisposer());
    }
}
