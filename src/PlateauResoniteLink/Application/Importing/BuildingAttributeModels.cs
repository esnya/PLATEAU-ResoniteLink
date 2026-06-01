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

internal abstract record BuildingMetricValue
{
    public static BuildingMetricValue Missing { get; } = new MissingMetricValue();

    public static BuildingMetricValue Known(double value)
    {
        return new KnownMetricValue(value);
    }

    public static BuildingMetricValue Invalid(string raw)
    {
        return new InvalidMetricValue(raw);
    }

    internal sealed record MissingMetricValue : BuildingMetricValue
    {
    }

    internal sealed record KnownMetricValue : BuildingMetricValue
    {
        public KnownMetricValue(double value)
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

    internal sealed record InvalidMetricValue : BuildingMetricValue
    {
        public InvalidMetricValue(string raw)
        {
            Raw = raw ?? throw new ArgumentNullException(nameof(raw));
        }

        public string Raw { get; }
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
