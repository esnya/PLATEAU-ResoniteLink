namespace PlateauResoniteLink.Core.Domain.Importing;

public sealed record BundledDefaultMaterialProfile(
    ScalarPair TextureScale,
    ScalarPair? TextureOffset = null,
    ScalarPair? ImplicitTextureScale = null,
    ScalarPair? ImplicitTextureOffset = null,
    BundledDefaultMaterialUvScaleSemantic ScaleSemantic = BundledDefaultMaterialUvScaleSemantic.WorldMeters)
{
    public ScalarPair GetImplicitTextureScale() => ImplicitTextureScale ?? TextureScale;

    public ScalarPair? GetImplicitTextureOffset() => ImplicitTextureOffset ?? TextureOffset;
}
