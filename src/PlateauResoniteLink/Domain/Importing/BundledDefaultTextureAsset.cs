using System;
using System.Collections.Concurrent;

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
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{FacadeRoot}Facade005_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{FacadeRoot}Facade005_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{FacadeRoot}Facade005_2K-JPG_NormalGL.jpg");
        }

        public static class Facade006
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{FacadeRoot}Facade006_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{FacadeRoot}Facade006_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{FacadeRoot}Facade006_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{FacadeRoot}Facade006_2K-JPG_NormalGL.jpg");
        }

        public static class Facade011
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{FacadeRoot}Facade011_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultEmissionTextureRole> Emission = EmissionAsset($"{FacadeRoot}Facade011_2K-JPG_Emission.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{FacadeRoot}Facade011_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{FacadeRoot}Facade011_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{FacadeRoot}Facade011_2K-JPG_NormalGL.jpg");
        }

        public static class Facade014
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{FacadeRoot}Facade014_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultEmissionTextureRole> Emission = EmissionAsset($"{FacadeRoot}Facade014_2K-JPG_Emission.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{FacadeRoot}Facade014_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{FacadeRoot}Facade014_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{FacadeRoot}Facade014_2K-JPG_NormalGL.jpg");
        }

        public static class Facade015
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{FacadeRoot}Facade015_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultEmissionTextureRole> Emission = EmissionAsset($"{FacadeRoot}Facade015_2K-JPG_Emission.jpg");
        }

        public static class Facade018A
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{FacadeRoot}Facade018A_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{FacadeRoot}Facade018A_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{FacadeRoot}Facade018A_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{FacadeRoot}Facade018A_2K-JPG_NormalGL.jpg");
        }

        public static class Facade019A
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{FacadeRoot}Facade019A_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{FacadeRoot}Facade019A_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{FacadeRoot}Facade019A_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{FacadeRoot}Facade019A_2K-JPG_NormalGL.jpg");
        }

        public static class Facade020A
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{FacadeRoot}Facade020A_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{FacadeRoot}Facade020A_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{FacadeRoot}Facade020A_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{FacadeRoot}Facade020A_2K-JPG_NormalGL.jpg");
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
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{RoofRoot}Concrete033_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{RoofRoot}Concrete033_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{RoofRoot}Concrete033_2K-JPG_NormalGL.jpg");
        }
    }

    public static class Roof
    {
        public static class RoofingTiles012A
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{RoofRoot}RoofingTiles012A_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{RoofRoot}RoofingTiles012A_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{RoofRoot}RoofingTiles012A_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{RoofRoot}RoofingTiles012A_2K-JPG_NormalGL.jpg");
        }

        public static class RoofingTiles014B
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{RoofRoot}RoofingTiles014B_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{RoofRoot}RoofingTiles014B_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{RoofRoot}RoofingTiles014B_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{RoofRoot}RoofingTiles014B_2K-JPG_NormalGL.jpg");
        }
    }

    public static class Road
    {
        public static class Road012A
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{RoadRoot}Road012A_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{RoadRoot}Road012A_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{RoadRoot}Road012A_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{RoadRoot}Road012A_2K-JPG_NormalGL.jpg");
        }

        public static class Road013A
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{RoadRoot}Road013A_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{RoadRoot}Road013A_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{RoadRoot}Road013A_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{RoadRoot}Road013A_2K-JPG_NormalGL.jpg");
        }

        public static class Road014A
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{RoadRoot}Road014A_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{RoadRoot}Road014A_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{RoadRoot}Road014A_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{RoadRoot}Road014A_2K-JPG_NormalGL.jpg");
        }

        public static class Road015A
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{RoadRoot}Road015A_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{RoadRoot}Road015A_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{RoadRoot}Road015A_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{RoadRoot}Road015A_2K-JPG_NormalGL.jpg");
        }
    }

    public static class Ground
    {
        public static class Ground054
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{OtherRoot}Ground054_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{OtherRoot}Ground054_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{OtherRoot}Ground054_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{OtherRoot}Ground054_2K-JPG_NormalGL.jpg");
        }
    }

    public static class Wall
    {
        public static class Plaster001
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallRoot}Plaster001_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallRoot}Plaster001_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallRoot}Plaster001_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallRoot}Plaster001_2K-JPG_NormalGL.jpg");
        }

        public static class Plaster002
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallRoot}Plaster002_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallRoot}Plaster002_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallRoot}Plaster002_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallRoot}Plaster002_2K-JPG_NormalGL.jpg");
        }

        public static class Plaster003
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallRoot}Plaster003_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallRoot}Plaster003_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallRoot}Plaster003_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallRoot}Plaster003_2K-JPG_NormalGL.jpg");
        }

        public static class Plaster004
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallRoot}Plaster004_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallRoot}Plaster004_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallRoot}Plaster004_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallRoot}Plaster004_2K-JPG_NormalGL.jpg");
        }

        public static class Plaster005
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallRoot}Plaster005_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallRoot}Plaster005_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallRoot}Plaster005_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallRoot}Plaster005_2K-JPG_NormalGL.jpg");
        }

        public static class Plaster006
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallRoot}Plaster006_2K-JPG_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallRoot}Plaster006_2K-JPG_Height.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallRoot}Plaster006_2K-JPG_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallRoot}Plaster006_2K-JPG_NormalGL.jpg");
        }
    }

    public static class TextureCanFacade
    {
        public static class Others0022
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{TextureCanFacadeRoot}Others0022_2K_Color.jpg");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{TextureCanFacadeRoot}Others0022_2K_Metallic.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{TextureCanFacadeRoot}Others0022_2K_NormalGL.png");
        }
    }

    public static class WallSkins
    {
        public static class ResidentialPlasterLow
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_res_plaster_low/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultEmissionTextureRole> Emission = EmissionAsset($"{WallSkinRoot}wall_res_plaster_low/emission.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_res_plaster_low/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_res_plaster_low/metallic_ao_smoothness.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_res_plaster_low/normalGL.png");
        }

        public static class ResidentialPlasterDark
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_res_plaster_dark/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_res_plaster_dark/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_res_plaster_dark/normalGL.png");
        }

        public static class ResidentialTileLow
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_res_tile_low/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultEmissionTextureRole> Emission = EmissionAsset($"{WallSkinRoot}wall_res_tile_low/emission.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_res_tile_low/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_res_tile_low/metallic_ao_smoothness.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_res_tile_low/normalGL.png");
        }

        public static class ResidentialTileDark
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_res_tile_dark/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_res_tile_dark/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_res_tile_dark/metallic_ao_smoothness.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_res_tile_dark/normalGL.png");
        }

        public static class ResidentialTileDarkIrregular
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_res_tile_dark_irregular/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_res_tile_dark_irregular/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_res_tile_dark_irregular/metallic_ao_smoothness.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_res_tile_dark_irregular/normalGL.png");
        }

        public static class ResidentialSidingBrickGray
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_res_siding_brick_gray/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_res_siding_brick_gray/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_res_siding_brick_gray/metallic_ao_smoothness.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_res_siding_brick_gray/normalGL.png");
        }

        public static class ApartmentTileMid
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_apartment_tile_mid/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_apartment_tile_mid/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_apartment_tile_mid/metallic_ao_smoothness.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_apartment_tile_mid/normalGL.png");
        }

        public static class ApartmentTileDark
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_apartment_tile_dark/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_apartment_tile_dark/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_apartment_tile_dark/metallic_ao_smoothness.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_apartment_tile_dark/normalGL.png");
        }

        public static class RcPaintedMid
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_rc_painted_mid/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_rc_painted_mid/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_rc_painted_mid/metallic_ao_smoothness.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_rc_painted_mid/normalGL.png");
        }

        public static class RcPaintedDark
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_rc_painted_dark/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_rc_painted_dark/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_rc_painted_dark/metallic_ao_smoothness.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_rc_painted_dark/normalGL.png");
        }

        public static class FactoryMetal
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_factory_metal/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_factory_metal/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_factory_metal/metallic_ao_smoothness.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_factory_metal/normalGL.png");
        }

        public static class CommercialPanel
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_commercial_panel/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_commercial_panel/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_commercial_panel/metallic_ao_smoothness.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_commercial_panel/normalGL.png");
        }

        public static class CommercialPanelDark
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_commercial_panel_dark/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_commercial_panel_dark/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_commercial_panel_dark/metallic_ao_smoothness.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_commercial_panel_dark/normalGL.png");
        }

        public static class SchoolPublicBand
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_school_public_band/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_school_public_band/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_school_public_band/metallic_ao_smoothness.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_school_public_band/normalGL.png");
        }

        public static class SchoolPublicDark
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_school_public_dark/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_school_public_dark/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_school_public_dark/metallic_ao_smoothness.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_school_public_dark/normalGL.png");
        }

        public static class BrickRetro
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_brick_retro/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_brick_retro/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_brick_retro/metallic_ao_smoothness.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_brick_retro/normalGL.png");
        }

        public static class BrickDark
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_brick_dark/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_brick_dark/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_brick_dark/metallic_ao_smoothness.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_brick_dark/normalGL.png");
        }

        public static class WoodRuralLight
        {
            public static readonly BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> Albedo = AlbedoAsset($"{WallSkinRoot}wall_wood_rural_light/basecolor.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultEmissionTextureRole> Emission = EmissionAsset($"{WallSkinRoot}wall_wood_rural_light/emission.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultHeightTextureRole> Height = HeightAsset($"{WallSkinRoot}wall_wood_rural_light/height.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole> Metallic = MetallicAsset($"{WallSkinRoot}wall_wood_rural_light/metallic_ao_smoothness.png");
            public static readonly BundledDefaultTextureAsset<BundledDefaultNormalTextureRole> Normal = NormalAsset($"{WallSkinRoot}wall_wood_rural_light/normalGL.png");
        }
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
}
