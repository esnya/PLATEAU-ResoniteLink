namespace Plateau.ResoniteLink.Domain.Importing;

public static class BundledDefaultMaterialFamilies
{
    public const string Facade = "facade";
    public const string Roof = "roof";
    public const string Road = "road";
    public const string Vegetation = "vegetation";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> FacadeVariants =
    [
        "default-materials/facade/Facade018C_2K-JPG_Color.jpg",
        "default-materials/facade/Facade019A_2K-JPG_Color.jpg",
        "default-materials/facade/Facade020A_2K-JPG_Color.jpg",
    ];

    public static readonly IReadOnlyList<string> RoofVariants =
    [
        "default-materials/roof/Concrete012_2K-JPG_Color.jpg",
        "default-materials/roof/Concrete033_2K-JPG_Color.jpg",
    ];

    public static readonly IReadOnlyList<string> RoadVariants =
    [
        "default-materials/road/Asphalt020L_2K-JPG_Color.jpg",
        "default-materials/road/Asphalt023L_2K-JPG_Color.jpg",
    ];

    public static readonly IReadOnlyList<string> VegetationVariants =
    [
        "default-materials/other/Ground054_2K-JPG_Color.jpg",
        "default-materials/other/Concrete012_2K-JPG_Color.jpg",
    ];

    public static readonly IReadOnlyList<string> OtherVariants =
    [
        "default-materials/other/Concrete012_2K-JPG_Color.jpg",
        "default-materials/other/Ground054_2K-JPG_Color.jpg",
    ];

    public static IReadOnlyList<string> GetVariants(string family)
    {
        return family switch
        {
            Facade => FacadeVariants,
            Roof => RoofVariants,
            Road => RoadVariants,
            Vegetation => VegetationVariants,
            Other => OtherVariants,
            _ => throw new InvalidOperationException($"Unknown bundled material family '{family}'."),
        };
    }
}
