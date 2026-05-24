using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class BuildingAttributeParser
{
    internal static BuildingAttributeContext Parse(XElement cityObjectElement)
    {
        ArgumentNullException.ThrowIfNull(cityObjectElement);

        return new BuildingAttributeContext(
            RoofShape: ParseOptionalRoofShape(GetFirstDirectElementValue(cityObjectElement, "roofType")),
            Uses: ParseBuildingUses(GetDirectElementValues(cityObjectElement, "usage")),
            DetailedUses: ParseBuildingUses(GetDescendantElementValues(cityObjectElement, "detailedUsage")),
            Structures: ParseBuildingStructures(GetDescendantElementValues(cityObjectElement, "buildingStructureType")),
            CityGmlClassCodes: GetDirectElementValues(cityObjectElement, "class"),
            CityGmlFunctionCodes: GetDirectElementValues(cityObjectElement, "function"),
            MeasuredHeightMeters: ParseMetricValue(GetFirstDirectElement(cityObjectElement, "measuredHeight"), requireMeters: true),
            StoreysAboveGround: ParseIntegerMetricValue(GetFirstDirectElement(cityObjectElement, "storeysAboveGround")),
            StoreysBelowGround: ParseIntegerMetricValue(GetFirstDirectElement(cityObjectElement, "storeysBelowGround")),
            BuildingFootprintArea: ParseMetricValue(GetFirstDescendantElement(cityObjectElement, "buildingFootprintArea"), requireMeters: false),
            BuildingRoofEdgeArea: ParseMetricValue(GetFirstDescendantElement(cityObjectElement, "buildingRoofEdgeArea"), requireMeters: false),
            BuildingHeight: ParseMetricValue(GetFirstDescendantElement(cityObjectElement, "buildingHeight"), requireMeters: true),
            EaveHeight: ParseMetricValue(GetFirstDescendantElement(cityObjectElement, "eaveHeight"), requireMeters: true));
    }

    private static XElement? GetFirstDirectElement(XElement element, string localName)
    {
        return element.Elements()
            .FirstOrDefault(child => string.Equals(child.Name.LocalName, localName, StringComparison.Ordinal));
    }

    private static XElement? GetFirstDescendantElement(XElement element, string localName)
    {
        return element.Descendants()
            .FirstOrDefault(child => string.Equals(child.Name.LocalName, localName, StringComparison.Ordinal));
    }

    private static string? GetFirstDirectElementValue(XElement element, string localName)
    {
        return GetFirstDirectElement(element, localName)?.Value.Trim();
    }

    private static string[] GetDirectElementValues(XElement element, string localName)
    {
        return element.Elements()
            .Where(child => string.Equals(child.Name.LocalName, localName, StringComparison.Ordinal))
            .Select(static child => child.Value.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static string[] GetDescendantElementValues(XElement element, string localName)
    {
        return element.Descendants()
            .Where(child => string.Equals(child.Name.LocalName, localName, StringComparison.Ordinal))
            .Select(static child => child.Value.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static BuildingCodeValue<CityGmlRoofShape>? ParseOptionalRoofShape(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        string code = rawValue.Trim();
        CityGmlRoofShape shape = code switch
        {
            "1" => CityGmlRoofShape.Gable,
            "2" => CityGmlRoofShape.Hip,
            "3" => CityGmlRoofShape.Pyramid,
            "4" => CityGmlRoofShape.Flat,
            "5" => CityGmlRoofShape.Shed,
            "6" => CityGmlRoofShape.HalfHip,
            "7" => CityGmlRoofShape.Irimoya,
            "9" => CityGmlRoofShape.Mansard,
            "12" => CityGmlRoofShape.Shed,
            "14" => CityGmlRoofShape.Sawtooth,
            "21" => CityGmlRoofShape.Gambrel,
            "23" => CityGmlRoofShape.Arch,
            "24" => CityGmlRoofShape.Dome,
            "26" => CityGmlRoofShape.Arch,
            "28" => CityGmlRoofShape.Other,
            "9020" => CityGmlRoofShape.Unknown,
            "99" => CityGmlRoofShape.Other,
            "9999" => CityGmlRoofShape.Unknown,
            _ => CityGmlRoofShape.Unknown,
        };
        return new BuildingCodeValue<CityGmlRoofShape>(shape, code);
    }

    private static BuildingCodeValue<PlateauBuildingUse>[] ParseBuildingUses(IEnumerable<string> rawValues)
    {
        return rawValues
            .Select(static rawValue => new BuildingCodeValue<PlateauBuildingUse>(MapBuildingUse(rawValue), rawValue))
            .ToArray();
    }

    private static PlateauBuildingUse MapBuildingUse(string code)
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

    private static BuildingCodeValue<PlateauBuildingStructure>[] ParseBuildingStructures(IEnumerable<string> rawValues)
    {
        return rawValues
            .Select(static rawValue => new BuildingCodeValue<PlateauBuildingStructure>(MapBuildingStructure(rawValue), rawValue))
            .ToArray();
    }

    private static PlateauBuildingStructure MapBuildingStructure(string code)
    {
        return code switch
        {
            "601" => PlateauBuildingStructure.Wood,
            "602" => PlateauBuildingStructure.SteelReinforcedConcrete,
            "603" => PlateauBuildingStructure.ReinforcedConcrete,
            "604" => PlateauBuildingStructure.Steel,
            "605" => PlateauBuildingStructure.LightweightSteel,
            "606" => PlateauBuildingStructure.ConcreteBlock,
            "607" => PlateauBuildingStructure.ConcreteBlock,
            "610" or "611" or "612" or "613" => PlateauBuildingStructure.NonWood,
            "9999" => PlateauBuildingStructure.Unknown,
            _ => PlateauBuildingStructure.Unknown,
        };
    }

    private static BuildingMetricValue ParseIntegerMetricValue(XElement? element)
    {
        if (element is null)
        {
            return BuildingMetricValue.Missing;
        }

        string rawValue = element.Value.Trim();
        if (IsPlateauMissingMetricToken(rawValue))
        {
            return BuildingMetricValue.Missing;
        }

        return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            && value >= 0
                ? BuildingMetricValue.Known(value)
                : BuildingMetricValue.Invalid(rawValue);
    }

    private static BuildingMetricValue ParseMetricValue(XElement? element, bool requireMeters)
    {
        if (element is null)
        {
            return BuildingMetricValue.Missing;
        }

        string rawValue = element.Value.Trim();
        if (IsPlateauMissingMetricToken(rawValue))
        {
            return BuildingMetricValue.Missing;
        }

        string? unitOfMeasure = element.Attribute("uom")?.Value.Trim();
        if (requireMeters
            && !string.IsNullOrWhiteSpace(unitOfMeasure)
            && !string.Equals(unitOfMeasure, "m", StringComparison.OrdinalIgnoreCase))
        {
            return BuildingMetricValue.Invalid(rawValue);
        }

        return double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && value > 0.0
                ? BuildingMetricValue.Known(value)
                : BuildingMetricValue.Invalid(rawValue);
    }

    private static bool IsPlateauMissingMetricToken(string rawValue)
    {
        return string.Equals(rawValue, "-9999", StringComparison.Ordinal)
            || string.Equals(rawValue, "9999", StringComparison.Ordinal)
            || string.Equals(rawValue, "0001", StringComparison.Ordinal);
    }
}
