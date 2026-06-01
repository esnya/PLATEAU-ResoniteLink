using System;
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

internal sealed record BuildingCodeValue<T>(T Value, string Code);

internal enum BuildingMetricKind
{
    MeasuredHeightMeters,
    StoreysAboveGround,
    StoreysBelowGround,
    BuildingFootprintArea,
    BuildingRoofEdgeArea,
    BuildingHeight,
    EaveHeight,
}

internal readonly record struct BuildingMetricValue
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

internal sealed record BuildingMetricMeasurements
{
    public static BuildingMetricMeasurements Empty { get; } = new(new Dictionary<BuildingMetricKind, BuildingMetricValue>());

    public BuildingMetricMeasurements(IReadOnlyDictionary<BuildingMetricKind, BuildingMetricValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = new Dictionary<BuildingMetricKind, BuildingMetricValue>(values);
    }

    public IReadOnlyDictionary<BuildingMetricKind, BuildingMetricValue> Values { get; }

    public bool TryGet(BuildingMetricKind kind, out BuildingMetricValue value)
    {
        return Values.TryGetValue(kind, out value);
    }
}

internal sealed record BuildingAttributeContext(
    BuildingCodeValue<CityGmlRoofShape>? RoofShape,
    IReadOnlyList<BuildingCodeValue<PlateauBuildingUse>> Uses,
    IReadOnlyList<BuildingCodeValue<PlateauBuildingUse>> DetailedUses,
    IReadOnlyList<BuildingCodeValue<PlateauBuildingStructure>> Structures,
    IReadOnlyList<string> CityGmlClassCodes,
    IReadOnlyList<string> CityGmlFunctionCodes,
    BuildingMetricMeasurements Metrics)
{
    public static BuildingAttributeContext Empty { get; } = new(
        RoofShape: null,
        Uses: [],
        DetailedUses: [],
        Structures: [],
        CityGmlClassCodes: [],
        CityGmlFunctionCodes: [],
        Metrics: BuildingMetricMeasurements.Empty);
}
