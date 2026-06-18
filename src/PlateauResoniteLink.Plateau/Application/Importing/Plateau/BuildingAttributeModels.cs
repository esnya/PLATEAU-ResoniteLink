using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Plateau.Application.Importing.Plateau;

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

internal sealed record BuildingCodeValue<T>(T Value, string Code);

internal sealed record BuildingMetricValue
{
    public BuildingMetricValue(double value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Value = value;
    }

    public double Value { get; }
}

internal sealed record BuildingAttributeContext(
    BuildingCodeValue<CityGmlRoofShape>? RoofShape,
    IReadOnlyList<BuildingCodeValue<PlateauBuildingUse>> Uses,
    IReadOnlyList<BuildingCodeValue<PlateauBuildingUse>> DetailedUses,
    IReadOnlyList<BuildingCodeValue<PlateauBuildingStructure>> Structures,
    IReadOnlyList<string> CityGmlClassCodes,
    IReadOnlyList<string> CityGmlFunctionCodes,
    BuildingMetricValue? MeasuredHeightMeters,
    BuildingMetricValue? StoreysAboveGround,
    BuildingMetricValue? StoreysBelowGround,
    BuildingMetricValue? BuildingFootprintArea,
    BuildingMetricValue? BuildingRoofEdgeArea,
    BuildingMetricValue? BuildingHeight,
    BuildingMetricValue? EaveHeight)
{
    public static BuildingAttributeContext Empty { get; } = new(
        RoofShape: null,
        Uses: [],
        DetailedUses: [],
        Structures: [],
        CityGmlClassCodes: [],
        CityGmlFunctionCodes: [],
        MeasuredHeightMeters: null,
        StoreysAboveGround: null,
        StoreysBelowGround: null,
        BuildingFootprintArea: null,
        BuildingRoofEdgeArea: null,
        BuildingHeight: null,
        EaveHeight: null);
}
