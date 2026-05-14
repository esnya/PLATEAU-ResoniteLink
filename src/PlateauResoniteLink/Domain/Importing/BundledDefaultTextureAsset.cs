using System;
using System.Collections.Concurrent;
using System.IO;

namespace PlateauResoniteLink.Domain.Importing;

public interface IBundledDefaultTextureRole
{
}

public sealed class BundledDefaultAlbedoTextureRole : IBundledDefaultTextureRole
{
    private BundledDefaultAlbedoTextureRole()
    {
    }
}

public sealed class BundledDefaultEmissionTextureRole : IBundledDefaultTextureRole
{
    private BundledDefaultEmissionTextureRole()
    {
    }
}

public sealed class BundledDefaultHeightTextureRole : IBundledDefaultTextureRole
{
    private BundledDefaultHeightTextureRole()
    {
    }
}

public sealed class BundledDefaultMetallicTextureRole : IBundledDefaultTextureRole
{
    private BundledDefaultMetallicTextureRole()
    {
    }
}

public sealed class BundledDefaultNormalTextureRole : IBundledDefaultTextureRole
{
    private BundledDefaultNormalTextureRole()
    {
    }
}

public abstract class BundledDefaultTextureAsset
{
    private protected BundledDefaultTextureAsset(string logicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalPath);
        LogicalPath = logicalPath;
    }

    internal string LogicalPath { get; }
}

public sealed class BundledDefaultTextureAsset<TRole> : BundledDefaultTextureAsset
    where TRole : IBundledDefaultTextureRole
{
    internal BundledDefaultTextureAsset(string logicalPath)
        : base(logicalPath)
    {
    }
}

public static class BundledDefaultTextureAssets
{
    private const string FacadeRoot = "default-materials/ambientcg/facade/";
    private const string RoofRoot = "default-materials/ambientcg/roof/";
    private const string OtherRoot = "default-materials/ambientcg/other/";
    private const string RoadRoot = "default-materials/ambientcg/road/";
    private const string WallRoot = "default-materials/ambientcg/wall/";
    private const string TextureCanFacadeRoot = "default-materials/texturecan/facade/";
    private const string WallSkinRoot = "default-materials/wallskins/";
    private static readonly ConcurrentDictionary<string, BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole>> AlbedoAssets = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, BundledDefaultTextureAsset<BundledDefaultEmissionTextureRole>> EmissionAssets = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, BundledDefaultTextureAsset<BundledDefaultHeightTextureRole>> HeightAssets = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole>> MetallicAssets = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, BundledDefaultTextureAsset<BundledDefaultNormalTextureRole>> NormalAssets = new(StringComparer.Ordinal);

    public static class Facade
    {
        public static class Facade001
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{FacadeRoot}Facade001_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{FacadeRoot}Facade001_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{FacadeRoot}Facade001_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{FacadeRoot}Facade001_2K-JPG_NormalGL.jpg");
        }

        public static class Facade002
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{FacadeRoot}Facade002_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultEmissionTextureRole> Emission = EmissionAsset($"{FacadeRoot}Facade002_2K-JPG_Emission.jpg");
        }

        public static class Facade005
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{FacadeRoot}Facade005_2K-JPG_Color.jpg");
        }

        public static class Facade006
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{FacadeRoot}Facade006_2K-JPG_Color.jpg");
        }

        public static class Facade011
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{FacadeRoot}Facade011_2K-JPG_Color.jpg");
        }

        public static class Facade014
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{FacadeRoot}Facade014_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{FacadeRoot}Facade014_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{FacadeRoot}Facade014_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{FacadeRoot}Facade014_2K-JPG_NormalGL.jpg");
        }

        public static class Facade015
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{FacadeRoot}Facade015_2K-JPG_Color.jpg");
        }

        public static class Facade018A
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{FacadeRoot}Facade018A_2K-JPG_Color.jpg");
        }

        public static class Facade019A
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{FacadeRoot}Facade019A_2K-JPG_Color.jpg");
        }

        public static class Facade020A
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{FacadeRoot}Facade020A_2K-JPG_Color.jpg");
        }
    }

    public static class Concrete
    {
        public static class Concrete012
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{OtherRoot}Concrete012_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{OtherRoot}Concrete012_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{OtherRoot}Concrete012_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{OtherRoot}Concrete012_2K-JPG_NormalGL.jpg");
        }

        public static class Concrete033
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{RoofRoot}Concrete033_2K-JPG_Color.jpg");
        }
    }

    public static class Roof
    {
        public static class RoofingTiles012A
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{RoofRoot}RoofingTiles012A_2K-JPG_Color.jpg");
        }

        public static class RoofingTiles014B
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{RoofRoot}RoofingTiles014B_2K-JPG_Color.jpg");
        }
    }

    public static class Road
    {
        public static class Road012A
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{RoadRoot}Road012A_2K-JPG_Color.jpg");
        }

        public static class Road013A
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{RoadRoot}Road013A_2K-JPG_Color.jpg");
        }

        public static class Road014A
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{RoadRoot}Road014A_2K-JPG_Color.jpg");
        }

        public static class Road015A
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{RoadRoot}Road015A_2K-JPG_Color.jpg");
        }
    }

    public static class Ground
    {
        public static class Ground054
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{OtherRoot}Ground054_2K-JPG_Color.jpg");
        }
    }

    public static class Wall
    {
        public static class Plaster001
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallRoot}Plaster001_2K-JPG_Color.jpg");
        }

        public static class Plaster002
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallRoot}Plaster002_2K-JPG_Color.jpg");
        }

        public static class Plaster003
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallRoot}Plaster003_2K-JPG_Color.jpg");
        }

        public static class Plaster004
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallRoot}Plaster004_2K-JPG_Color.jpg");
        }

        public static class Plaster005
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallRoot}Plaster005_2K-JPG_Color.jpg");
        }

        public static class Plaster006
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallRoot}Plaster006_2K-JPG_Color.jpg");
        }
    }

    public static class TextureCanFacade
    {
        public static class Others0022
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{TextureCanFacadeRoot}Others0022_2K_Color.jpg");
        }
    }

    public static class WallSkins
    {
        public static class ResidentialPlasterLow
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_res_plaster_low/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultEmissionTextureRole> Emission = EmissionAsset($"{WallSkinRoot}wall_res_plaster_low/emission.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_res_plaster_low/metallic_ao_smoothness.png");
        }

        public static class ResidentialPlasterDark
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_res_plaster_dark/basecolor.png");
        }

        public static class ResidentialTileLow
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_res_tile_low/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultEmissionTextureRole> Emission = EmissionAsset($"{WallSkinRoot}wall_res_tile_low/emission.png");
        }

        public static class ResidentialTileDark
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_res_tile_dark/basecolor.png");
        }

        public static class ResidentialTileDarkIrregular
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_res_tile_dark_irregular/basecolor.png");
        }

        public static class ResidentialSidingBrickGray
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_res_siding_brick_gray/basecolor.png");
        }

        public static class ApartmentTileMid
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_apartment_tile_mid/basecolor.png");
        }

        public static class ApartmentTileDark
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_apartment_tile_dark/basecolor.png");
        }

        public static class RcPaintedMid
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_rc_painted_mid/basecolor.png");
        }

        public static class RcPaintedDark
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_rc_painted_dark/basecolor.png");
        }

        public static class FactoryMetal
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_factory_metal/basecolor.png");
        }

        public static class CommercialPanel
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_commercial_panel/basecolor.png");
        }

        public static class CommercialPanelDark
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_commercial_panel_dark/basecolor.png");
        }

        public static class SchoolPublicBand
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_school_public_band/basecolor.png");
        }

        public static class SchoolPublicDark
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_school_public_dark/basecolor.png");
        }

        public static class BrickRetro
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_brick_retro/basecolor.png");
        }

        public static class BrickDark
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_brick_dark/basecolor.png");
        }

        public static class WoodRuralLight
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_wood_rural_light/basecolor.png");
        }
    }

    internal static bool TryGetCompanionAsset<TRole>(
        BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> albedo,
        out BundledDefaultTextureAsset<TRole>? asset)
        where TRole : IBundledDefaultTextureRole
    {
        ArgumentNullException.ThrowIfNull(albedo);

        string? companionLogicalPath = TryCreateCompanionLogicalPath(albedo.LogicalPath, typeof(TRole));
        if (companionLogicalPath is null || IsBlackEmissionLogicalPath(companionLogicalPath))
        {
            asset = null;
            return false;
        }

        asset = CreateAsset<TRole>(companionLogicalPath);
        return true;
    }

    internal static bool IsBlackEmission(BundledDefaultTextureAsset<BundledDefaultEmissionTextureRole> asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return IsBlackEmissionLogicalPath(asset.LogicalPath);
    }

    internal static BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> AlbedoAsset(string logicalPath)
        => AlbedoAssets.GetOrAdd(logicalPath, static path => new(path));

    internal static BundledDefaultTextureAsset<BundledDefaultEmissionTextureRole> EmissionAsset(string logicalPath)
        => EmissionAssets.GetOrAdd(logicalPath, static path => new(path));

    internal static BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> HeightAsset(string logicalPath)
        => HeightAssets.GetOrAdd(logicalPath, static path => new(path));

    internal static BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> MetallicAsset(string logicalPath)
        => MetallicAssets.GetOrAdd(logicalPath, static path => new(path));

    internal static BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> NormalAsset(string logicalPath)
        => NormalAssets.GetOrAdd(logicalPath, static path => new(path));

    private static BundledDefaultTextureAsset<TRole> CreateAsset<TRole>(string logicalPath)
        where TRole : IBundledDefaultTextureRole
    {
        if (typeof(TRole) == typeof(BundledDefaultEmissionTextureRole))
        {
            return (BundledDefaultTextureAsset<TRole>)(BundledDefaultTextureAsset)EmissionAsset(logicalPath);
        }

        if (typeof(TRole) == typeof(BundledDefaultHeightTextureRole))
        {
            return (BundledDefaultTextureAsset<TRole>)(BundledDefaultTextureAsset)HeightAsset(logicalPath);
        }

        if (typeof(TRole) == typeof(BundledDefaultMetallicTextureRole))
        {
            return (BundledDefaultTextureAsset<TRole>)(BundledDefaultTextureAsset)MetallicAsset(logicalPath);
        }

        if (typeof(TRole) == typeof(BundledDefaultNormalTextureRole))
        {
            return (BundledDefaultTextureAsset<TRole>)(BundledDefaultTextureAsset)NormalAsset(logicalPath);
        }

        throw new InvalidOperationException($"Unsupported bundled texture role '{typeof(TRole).Name}'.");
    }

    private static string? TryCreateCompanionLogicalPath(string albedoLogicalPath, Type role)
    {
        string stem = Path.GetFileNameWithoutExtension(albedoLogicalPath);
        string directory = Path.GetDirectoryName(albedoLogicalPath)?.Replace('\\', '/')
            ?? throw new InvalidOperationException($"Could not determine bundled texture directory for '{albedoLogicalPath}'.");

        if (string.Equals(stem, "basecolor", StringComparison.Ordinal))
        {
            string? wallSkinFileName = role == typeof(BundledDefaultEmissionTextureRole)
                ? "emission.png"
                : role == typeof(BundledDefaultHeightTextureRole)
                    ? "height.png"
                    : role == typeof(BundledDefaultMetallicTextureRole)
                        ? "metallic_ao_smoothness.png"
                        : role == typeof(BundledDefaultNormalTextureRole)
                            ? "normalGL.png"
                            : null;
            return wallSkinFileName is null ? null : $"{directory}/{wallSkinFileName}";
        }

        if (!stem.EndsWith("_Color", StringComparison.Ordinal))
        {
            return null;
        }

        string baseStem = stem[..^"_Color".Length];
        string? suffix = role == typeof(BundledDefaultEmissionTextureRole)
            ? "_Emission.jpg"
            : role == typeof(BundledDefaultHeightTextureRole)
                ? "_Height.jpg"
                : role == typeof(BundledDefaultMetallicTextureRole)
                    ? "_Metallic.png"
                    : role == typeof(BundledDefaultNormalTextureRole)
                        ? "_NormalGL.jpg"
                        : null;
        return suffix is null ? null : $"{directory}/{baseStem}{suffix}";
    }

    private static bool IsBlackEmissionLogicalPath(string logicalPath)
    {
        return logicalPath is
            $"{FacadeRoot}Facade018A_2K-JPG_Emission.jpg" or
            $"{FacadeRoot}Facade019A_2K-JPG_Emission.jpg" or
            $"{FacadeRoot}Facade020A_2K-JPG_Emission.jpg" or
            $"{WallSkinRoot}wall_apartment_tile_dark/emission.png" or
            $"{WallSkinRoot}wall_apartment_tile_mid/emission.png" or
            $"{WallSkinRoot}wall_brick_dark/emission.png" or
            $"{WallSkinRoot}wall_brick_retro/emission.png" or
            $"{WallSkinRoot}wall_commercial_panel/emission.png" or
            $"{WallSkinRoot}wall_commercial_panel_dark/emission.png" or
            $"{WallSkinRoot}wall_factory_metal/emission.png" or
            $"{WallSkinRoot}wall_rc_painted_dark/emission.png" or
            $"{WallSkinRoot}wall_rc_painted_mid/emission.png" or
            $"{WallSkinRoot}wall_school_public_band/emission.png" or
            $"{WallSkinRoot}wall_school_public_dark/emission.png";
    }
}
