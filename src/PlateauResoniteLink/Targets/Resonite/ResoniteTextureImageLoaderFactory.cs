namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteTextureImageLoaderFactory
{
    ResoniteTextureImageLoader Create();
}

internal sealed class ResoniteTextureImageLoaderFactory : IResoniteTextureImageLoaderFactory
{
    public ResoniteTextureImageLoader Create()
    {
        return new ResoniteTextureImageLoader();
    }
}
