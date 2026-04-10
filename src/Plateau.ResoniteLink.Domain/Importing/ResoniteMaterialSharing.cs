namespace Plateau.ResoniteLink.Domain.Importing;

public static class ResoniteMaterialSharing
{
    private static readonly ResoniteColor SharedAlbedoOnlyBaseColor = new(1.0, 1.0, 1.0, 1.0);

    public static bool CanUseSharedAlbedoOnlyMaterial(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);

        return material.MaterialType == ResoniteMaterialType.Standard
            && material.Projection == ResoniteMaterialProjection.Uv
            && material.TextureSourceKind == ResoniteTextureSourceKind.Dataset
            && !string.IsNullOrWhiteSpace(material.TexturePath)
            && material.DepthOffset is null
            && material.TextureScale is null
            && material.TextureOffset is null
            && material.BaseColor == SharedAlbedoOnlyBaseColor;
    }
}
