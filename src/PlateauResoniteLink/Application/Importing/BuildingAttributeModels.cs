using System.Collections.Generic;

namespace PlateauResoniteLink.Application.Importing;

internal enum CityGmlRoofShape
{
    Unknown = 0,
    Flat,
    Shed,
    Gable,
    Hip,
    Pyramid,
    HalfHip,
    Gambrel,
    Mansard,
    Sawtooth,
    Arch,
    Dome,
    Irimoya,
    Other,
}

internal enum PlateauBuildingUse
{
    Unknown = 0,
    DetachedResidential,
    Apartment,
    MixedResidential,
    Commercial,
    Office,
    Warehouse,
    Factory,
    Public,
    Education,
    Transport,
    Other,
}

internal enum PlateauBuildingStructure
{
    Unknown = 0,
    Wood,
    Steel,
    ReinforcedConcrete,
    SteelReinforcedConcrete,
    ConcreteBlock,
    LightweightSteel,
    NonWood,
    Other,
}

internal enum BuildingMetricValueKind
{
    Missing = 0,
    Known,
    Invalid,
}

internal sealed record BuildingCodeValue<T>(T Value, string Code);

internal sealed record BuildingMetricValue(BuildingMetricValueKind Kind, double? Value, string? Raw)
{
    public static BuildingMetricValue Missing { get; } = new(BuildingMetricValueKind.Missing, null, null);

    public static BuildingMetricValue Known(double value)
    {
        return new BuildingMetricValue(BuildingMetricValueKind.Known, value, null);
    }

    public static BuildingMetricValue Invalid(string raw)
    {
        return new BuildingMetricValue(BuildingMetricValueKind.Invalid, null, raw);
    }
}

internal sealed record BuildingAttributeContext(
    BuildingCodeValue<CityGmlRoofShape>? RoofShape,
    IReadOnlyList<BuildingCodeValue<PlateauBuildingUse>> Uses,
    IReadOnlyList<BuildingCodeValue<PlateauBuildingUse>> DetailedUses,
    IReadOnlyList<BuildingCodeValue<PlateauBuildingStructure>> Structures,
    IReadOnlyList<string> CityGmlClassCodes,
    IReadOnlyList<string> CityGmlFunctionCodes,
    BuildingMetricValue MeasuredHeightMeters,
    BuildingMetricValue StoreysAboveGround,
    BuildingMetricValue StoreysBelowGround,
    BuildingMetricValue BuildingFootprintArea,
    BuildingMetricValue BuildingRoofEdgeArea,
    BuildingMetricValue BuildingHeight,
    BuildingMetricValue EaveHeight)
{
    public static BuildingAttributeContext Empty { get; } = new(
        RoofShape: null,
        Uses: [],
        DetailedUses: [],
        Structures: [],
        CityGmlClassCodes: [],
        CityGmlFunctionCodes: [],
        MeasuredHeightMeters: BuildingMetricValue.Missing,
        StoreysAboveGround: BuildingMetricValue.Missing,
        StoreysBelowGround: BuildingMetricValue.Missing,
        BuildingFootprintArea: BuildingMetricValue.Missing,
        BuildingRoofEdgeArea: BuildingMetricValue.Missing,
        BuildingHeight: BuildingMetricValue.Missing,
        EaveHeight: BuildingMetricValue.Missing);
}
