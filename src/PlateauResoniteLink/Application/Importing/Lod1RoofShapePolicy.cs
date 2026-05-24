using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PlateauResoniteLink.Application.Importing;

internal static class Lod1RoofShapePolicy
{
    internal static GeneratedLod1RoofShape Select(
        string slotKey,
        BuildingAttributeContext attributes,
        double geometryHeightMeters,
        double lengthMeters,
        double widthMeters)
    {
        CityGmlRoofShape? explicitRoofShape = attributes.RoofShape?.Value;
        if (explicitRoofShape is not null and not CityGmlRoofShape.Unknown and not CityGmlRoofShape.Other)
        {
            return explicitRoofShape switch
            {
                CityGmlRoofShape.Flat => GeneratedLod1RoofShape.Flat,
                CityGmlRoofShape.Shed => GeneratedLod1RoofShape.Shed,
                CityGmlRoofShape.Gable => GeneratedLod1RoofShape.Gable,
                CityGmlRoofShape.Hip or CityGmlRoofShape.Pyramid or CityGmlRoofShape.HalfHip or CityGmlRoofShape.Irimoya => GeneratedLod1RoofShape.Hip,
                _ => GeneratedLod1RoofShape.Flat,
            };
        }

        double heightMeters = BuildingAttributeQueries.TryGetKnownPositiveMetric(attributes.MeasuredHeightMeters)
            ?? BuildingAttributeQueries.TryGetKnownPositiveMetric(attributes.BuildingHeight)
            ?? geometryHeightMeters;
        int? floorCount = BuildingAttributeQueries.TryGetKnownPositiveInteger(attributes.StoreysAboveGround);
        double footprintArea = BuildingAttributeQueries.TryGetKnownPositiveMetric(attributes.BuildingFootprintArea)
            ?? lengthMeters * widthMeters;
        bool residential = BuildingAttributeQueries.HasUse(attributes, PlateauBuildingUse.DetachedResidential)
            || BuildingAttributeQueries.HasUse(attributes, PlateauBuildingUse.MixedResidential);
        bool apartmentOrUrban = BuildingAttributeQueries.HasUse(attributes, PlateauBuildingUse.Apartment)
            || BuildingAttributeQueries.HasUse(attributes, PlateauBuildingUse.Office)
            || BuildingAttributeQueries.HasUse(attributes, PlateauBuildingUse.Commercial)
            || BuildingAttributeQueries.HasUse(attributes, PlateauBuildingUse.Public);
        bool industrial = BuildingAttributeQueries.HasUse(attributes, PlateauBuildingUse.Warehouse)
            || BuildingAttributeQueries.HasUse(attributes, PlateauBuildingUse.Factory);
        bool wood = attributes.Structures.Any(static structure => structure.Value == PlateauBuildingStructure.Wood);
        bool nonWood = attributes.Structures.Any(static structure => structure.Value is PlateauBuildingStructure.NonWood
            or PlateauBuildingStructure.ReinforcedConcrete
            or PlateauBuildingStructure.SteelReinforcedConcrete);

        if ((floorCount >= 4 || heightMeters > 12.0 || (apartmentOrUrban && nonWood))
            && !industrial)
        {
            return GeneratedLod1RoofShape.Flat;
        }

        if (industrial)
        {
            return heightMeters <= 12.0 ? GeneratedLod1RoofShape.Gable : GeneratedLod1RoofShape.Flat;
        }

        double aspectRatio = lengthMeters / Math.Max(widthMeters, 1e-6);
        if (aspectRatio >= 1.8)
        {
            return GeneratedLod1RoofShape.Shed;
        }

        if ((residential || wood) && footprintArea <= 250.0)
        {
            return aspectRatio <= 1.2 ? GeneratedLod1RoofShape.Hip : GeneratedLod1RoofShape.Gable;
        }

        if (footprintArea >= 350.0)
        {
            return GeneratedLod1RoofShape.Hip;
        }

        return StableModulo(slotKey, divisor: 3) == 0
            ? GeneratedLod1RoofShape.Shed
            : GeneratedLod1RoofShape.Gable;
    }

    private static int StableModulo(string value, int divisor)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToUInt16(hash, startIndex: 0) % divisor;
    }
}
