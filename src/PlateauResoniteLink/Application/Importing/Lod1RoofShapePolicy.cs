using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PlateauResoniteLink.Application.Importing;

internal static class Lod1RoofShapePolicy
{
    internal static Lod1RoofShape Select(
        string slotKey,
        BuildingAttributeContext attributes,
        double lengthMeters,
        double widthMeters,
        double geometryHeightMeters)
    {
        ArgumentNullException.ThrowIfNull(slotKey);
        ArgumentNullException.ThrowIfNull(attributes);

        CityGmlRoofShape? explicitRoofShape = attributes.RoofShape?.Value;
        if (explicitRoofShape is not null and not CityGmlRoofShape.Unknown and not CityGmlRoofShape.Other)
        {
            return explicitRoofShape switch
            {
                CityGmlRoofShape.Flat => Lod1RoofShape.Flat,
                CityGmlRoofShape.Shed => Lod1RoofShape.Shed,
                CityGmlRoofShape.Gable => Lod1RoofShape.Gable,
                CityGmlRoofShape.Hip or CityGmlRoofShape.Pyramid or CityGmlRoofShape.HalfHip or CityGmlRoofShape.Irimoya => Lod1RoofShape.Hip,
                _ => Lod1RoofShape.Flat,
            };
        }

        double heightMeters = BuildingMetricNormalizer.TryGetKnownPositiveMetric(attributes.MeasuredHeightMeters)
            ?? BuildingMetricNormalizer.TryGetKnownPositiveMetric(attributes.BuildingHeight)
            ?? geometryHeightMeters;
        int? floorCount = BuildingMetricNormalizer.TryGetKnownPositiveInteger(attributes.StoreysAboveGround);
        double footprintArea = BuildingMetricNormalizer.TryGetKnownPositiveMetric(attributes.BuildingFootprintArea)
            ?? lengthMeters * widthMeters;
        bool residential = BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.DetachedResidential)
            || BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.MixedResidential);
        bool apartmentOrUrban = BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Apartment)
            || BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Office)
            || BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Commercial)
            || BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Public);
        bool industrial = BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Warehouse)
            || BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Factory);
        bool wood = attributes.Structures.Any(static structure => structure.Value == PlateauBuildingStructure.Wood);
        bool nonWood = attributes.Structures.Any(static structure => structure.Value is PlateauBuildingStructure.NonWood
            or PlateauBuildingStructure.ReinforcedConcrete
            or PlateauBuildingStructure.SteelReinforcedConcrete);

        if ((floorCount >= 4 || heightMeters > 12.0 || (apartmentOrUrban && nonWood))
            && !industrial)
        {
            return Lod1RoofShape.Flat;
        }

        if (industrial)
        {
            return heightMeters <= 12.0 ? Lod1RoofShape.Gable : Lod1RoofShape.Flat;
        }

        double aspectRatio = lengthMeters / Math.Max(widthMeters, 1e-6);
        if (aspectRatio >= 1.8)
        {
            return Lod1RoofShape.Shed;
        }

        if ((residential || wood) && footprintArea <= 250.0)
        {
            return aspectRatio <= 1.2 ? Lod1RoofShape.Hip : Lod1RoofShape.Gable;
        }

        if (footprintArea >= 350.0)
        {
            return Lod1RoofShape.Hip;
        }

        return StableModulo(slotKey, divisor: 3) == 0
            ? Lod1RoofShape.Shed
            : Lod1RoofShape.Gable;
    }

    private static int StableModulo(string value, int divisor)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToUInt16(hash, startIndex: 0) % divisor;
    }
}
