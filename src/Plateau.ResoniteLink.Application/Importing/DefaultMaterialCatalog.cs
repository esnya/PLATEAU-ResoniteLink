using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal static class DefaultMaterialCatalog
{
    public static ResolvedMaterial ResolveMaterial(
        string packageName,
        string? texturePath,
        bool preferUvProjection,
        string variantSelectionKey)
    {
        if (!string.IsNullOrWhiteSpace(texturePath))
        {
            return new ResolvedMaterial(
                texturePath,
                ResoniteTextureSourceKind.Dataset,
                ResoniteMaterialProjection.Uv,
                Family: null);
        }

        string family = ResolveBundledTextureFamily(packageName, preferUvProjection);
        return new ResolvedMaterial(
            SelectBundledTexturePath(family, variantSelectionKey),
            ResoniteTextureSourceKind.Bundled,
            preferUvProjection ? ResoniteMaterialProjection.Uv : ResoniteMaterialProjection.Triplanar,
            family);
    }

    private static string ResolveBundledTextureFamily(string packageName, bool preferUvProjection)
    {
        if (preferUvProjection)
        {
            return BundledDefaultMaterialFamilies.Facade;
        }

        return packageName switch
        {
            "bldg" or "ubld" => BundledDefaultMaterialFamilies.Roof,
            "tran" or "rwy" or "squr" or "trk" => BundledDefaultMaterialFamilies.Road,
            _ => BundledDefaultMaterialFamilies.Other,
        };
    }

    private static string SelectBundledTexturePath(string family, string variantSelectionKey)
    {
        IReadOnlyList<string> variants = BundledDefaultMaterialFamilies.GetVariants(family);
        int hashCode = StringComparer.Ordinal.GetHashCode(variantSelectionKey) & int.MaxValue;
        int index = hashCode % variants.Count;
        return variants[index];
    }

    internal sealed record ResolvedMaterial(
        string? TexturePath,
        ResoniteTextureSourceKind TextureSourceKind,
        ResoniteMaterialProjection Projection,
        string? Family);
}
