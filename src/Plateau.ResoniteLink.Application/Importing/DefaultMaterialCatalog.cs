using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal static class DefaultMaterialCatalog
{
    public static ResolvedMaterial ResolveMaterial(
        string packageName,
        string? texturePath,
        bool preferUvProjection,
        string? familyOverride,
        string variantSelectionKey)
    {
        if (ShouldUseWireframeMaterial(packageName))
        {
            return new ResolvedMaterial(
                ResoniteMaterialType.Wireframe,
                TexturePath: null,
                ResoniteTextureSourceKind.Bundled,
                ResoniteMaterialProjection.Uv,
                Family: null,
                TextureScale: null);
        }

        if (!string.IsNullOrWhiteSpace(texturePath))
        {
            return new ResolvedMaterial(
                ResoniteMaterialType.Standard,
                texturePath,
                ResoniteTextureSourceKind.Dataset,
                ResoniteMaterialProjection.Uv,
                Family: null,
                TextureScale: null);
        }

        string family = familyOverride ?? ResolveBundledTextureFamily(packageName, preferUvProjection);
        string selectedTexturePath = SelectBundledTexturePath(family, variantSelectionKey);
        return new ResolvedMaterial(
            ResoniteMaterialType.Standard,
            selectedTexturePath,
            ResoniteTextureSourceKind.Bundled,
            preferUvProjection ? ResoniteMaterialProjection.Uv : ResoniteMaterialProjection.Triplanar,
            family,
            BundledDefaultMaterialProfiles.GetTilesPerMeter(selectedTexturePath));
    }

    private static bool ShouldUseWireframeMaterial(string packageName)
    {
        return packageName switch
        {
            "area" or "fld" or "htd" or "ifld" or "lsld" or "luse" or "rfld" or "tnm" or "urf" => true,
            _ => false,
        };
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
        ResoniteMaterialType MaterialType,
        string? TexturePath,
        ResoniteTextureSourceKind TextureSourceKind,
        ResoniteMaterialProjection Projection,
        string? Family,
        ResoniteFloat2? TextureScale);
}
