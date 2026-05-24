namespace PlateauResoniteLink.Application.Importing;

internal static class BuildingAttributeCodeMapper
{
    internal static PlateauBuildingUse MapUse(string code)
    {
        string broadCode = code.Length >= 3 ? code[..3] : code;
        PlateauBuildingUse broadUse = broadCode switch
        {
            "411" => PlateauBuildingUse.DetachedResidential,
            "412" => PlateauBuildingUse.Apartment,
            "413" or "414" or "415" => PlateauBuildingUse.MixedResidential,
            "401" => PlateauBuildingUse.Office,
            "402" or "403" or "404" => PlateauBuildingUse.Commercial,
            "431" => PlateauBuildingUse.Warehouse,
            "441" => PlateauBuildingUse.Factory,
            "422" => PlateauBuildingUse.Education,
            "421" or "452" or "453" => PlateauBuildingUse.Public,
            "454" or "451" or "471" => PlateauBuildingUse.Other,
            "461" or "999" => PlateauBuildingUse.Unknown,
            _ => PlateauBuildingUse.Unknown,
        };

        return broadUse is not PlateauBuildingUse.Unknown
            ? broadUse
            : code switch
            {
                "1110" => PlateauBuildingUse.DetachedResidential,
                "1120" or "1130" => PlateauBuildingUse.Apartment,
                "1140" or "1150" or "1160" => PlateauBuildingUse.MixedResidential,
                "1310" => PlateauBuildingUse.Office,
                "1510" or "1520" => PlateauBuildingUse.Commercial,
                "1710" or "1720" => PlateauBuildingUse.Warehouse,
                "1740" => PlateauBuildingUse.Factory,
                "1810" => PlateauBuildingUse.Education,
                "1910" or "1920" or "1930" => PlateauBuildingUse.Public,
                _ => PlateauBuildingUse.Unknown,
            };
    }
}
