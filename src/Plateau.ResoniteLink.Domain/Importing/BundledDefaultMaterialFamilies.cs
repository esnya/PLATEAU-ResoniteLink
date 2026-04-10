namespace Plateau.ResoniteLink.Domain.Importing;

public static class BundledDefaultMaterialFamilies
{
    public const string Facade = "facade";
    public const string Roof = "roof";
    public const string Road = "road";
    public const string Vegetation = "vegetation";
    public const string CityFurniture = "city-furniture";
    public const string Other = "other";

    public static IReadOnlyList<string> FacadeVariants { get; } = Array.AsReadOnly(
    [
        "default-materials/facade/Facade001_2K-JPG_Color.jpg",
        "default-materials/facade/Facade018A_2K-JPG_Color.jpg",
        "default-materials/facade/Facade019A_2K-JPG_Color.jpg",
        "default-materials/facade/Facade020A_2K-JPG_Color.jpg",
    ]);

    public static IReadOnlyList<string> RoofVariants { get; } = Array.AsReadOnly(
    [
        "default-materials/roof/Concrete012_2K-JPG_Color.jpg",
        "default-materials/roof/Concrete033_2K-JPG_Color.jpg",
        "default-materials/roof/RoofingTiles012A_2K-JPG_Color.jpg",
        "default-materials/roof/RoofingTiles014B_2K-JPG_Color.jpg",
    ]);

    public static IReadOnlyList<string> RoadVariants { get; } = Array.AsReadOnly(
    [
        "default-materials/road/Asphalt020L_2K-JPG_Color.jpg",
        "default-materials/road/Asphalt023L_2K-JPG_Color.jpg",
    ]);

    public static IReadOnlyList<string> VegetationVariants { get; } = Array.AsReadOnly(
    [
        "default-materials/other/Ground054_2K-JPG_Color.jpg",
        "default-materials/other/Concrete012_2K-JPG_Color.jpg",
    ]);

    public static IReadOnlyList<string> CityFurnitureVariants { get; } = Array.AsReadOnly(
    [
        "default-materials/city-furniture/Plaster001_2K-JPG_Color.jpg",
    ]);

    public static IReadOnlyList<string> OtherVariants { get; } = Array.AsReadOnly(
    [
        "default-materials/other/Concrete012_2K-JPG_Color.jpg",
        "default-materials/other/Ground054_2K-JPG_Color.jpg",
    ]);

    public static IReadOnlyList<string> GetVariants(string family)
    {
        return family switch
        {
            Facade => FacadeVariants,
            Roof => RoofVariants,
            Road => RoadVariants,
            Vegetation => VegetationVariants,
            CityFurniture => CityFurnitureVariants,
            Other => OtherVariants,
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown bundled material family."),
        };
    }
}
