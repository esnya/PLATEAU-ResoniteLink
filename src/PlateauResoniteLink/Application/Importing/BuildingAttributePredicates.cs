using System;
using System.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class BuildingAttributePredicates
{
    public static bool HasFacadeLikeMidriseUse(BuildingAttributeContext attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        return HasUse(attributes, PlateauBuildingUse.Office)
            || HasUse(attributes, PlateauBuildingUse.Commercial)
            || HasUse(attributes, PlateauBuildingUse.Public)
            || HasUse(attributes, PlateauBuildingUse.Education)
            || HasUse(attributes, PlateauBuildingUse.Apartment)
            || HasUse(attributes, PlateauBuildingUse.MixedResidential)
            || HasRawBuildingCode(attributes, "403");
    }

    public static bool HasNightOccupancy(BuildingAttributeContext attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        return HasUse(attributes, PlateauBuildingUse.Apartment)
            || HasUse(attributes, PlateauBuildingUse.MixedResidential)
            || HasRawBuildingCode(attributes, "403");
    }

    public static bool IsRobustStructure(BuildingAttributeContext attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        return attributes.Structures.Any(static structure => structure.Value is PlateauBuildingStructure.ReinforcedConcrete
            or PlateauBuildingStructure.SteelReinforcedConcrete
            or PlateauBuildingStructure.NonWood);
    }

    public static bool HasBrickLikeStructure(BuildingAttributeContext attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        return attributes.Structures.Any(static structure => structure.Value is PlateauBuildingStructure.ConcreteBlock);
    }

    public static bool HasUse(BuildingAttributeContext attributes, PlateauBuildingUse use)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        return attributes.Uses.Any(candidate => candidate.Value == use)
            || attributes.DetailedUses.Any(candidate => candidate.Value == use)
            || attributes.CityGmlFunctionCodes.Any(code => HasRawBuildingCode(code, use));
    }

    public static bool HasRawBuildingCode(BuildingAttributeContext attributes, string code)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return attributes.CityGmlFunctionCodes.Any(candidate => IsSameBroadCode(candidate, code))
            || attributes.CityGmlClassCodes.Any(candidate => IsSameBroadCode(candidate, code));
    }

    public static bool HasExactCityGmlClassCode(BuildingAttributeContext attributes, string code)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return attributes.CityGmlClassCodes.Any(candidate => string.Equals(candidate.Trim(), code, StringComparison.Ordinal));
    }

    private static bool HasRawBuildingCode(string code, PlateauBuildingUse use)
    {
        string broadCode = CreateBroadBuildingCode(code);
        return use switch
        {
            PlateauBuildingUse.DetachedResidential => broadCode is "411" or "111",
            PlateauBuildingUse.Apartment => broadCode is "412" or "112" or "113",
            PlateauBuildingUse.MixedResidential => broadCode is "413" or "414" or "415" or "114" or "115" or "116",
            PlateauBuildingUse.Office => broadCode is "401" or "131",
            PlateauBuildingUse.Commercial => broadCode is "402" or "403" or "404" or "151" or "152",
            PlateauBuildingUse.Warehouse => broadCode is "431" or "171" or "172",
            PlateauBuildingUse.Factory => broadCode is "441" or "174",
            PlateauBuildingUse.Education => broadCode is "422" or "181",
            PlateauBuildingUse.Public => broadCode is "421" or "191" or "192" or "193",
            _ => false,
        };
    }

    private static bool IsSameBroadCode(string candidate, string expected)
    {
        return string.Equals(CreateBroadBuildingCode(candidate), expected, StringComparison.Ordinal);
    }

    private static string CreateBroadBuildingCode(string code)
    {
        string trimmed = code.Trim();
        return trimmed.Length <= 3 ? trimmed : trimmed[..3];
    }
}
