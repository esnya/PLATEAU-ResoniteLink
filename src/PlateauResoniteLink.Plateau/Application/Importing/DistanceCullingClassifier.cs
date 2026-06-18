using System;

using PlateauResoniteLink.Core.Application.Importing.Contracts;
using PlateauResoniteLink.Plateau.Application.Importing.Source;
using PlateauResoniteLink.Plateau.Application.Importing.Plateau;

namespace PlateauResoniteLink.Plateau.Application.Importing;

internal static class DistanceCullingClassifier
{
    public static DistanceCullingClass? Classify(
        string packageName,
        int? lodLevel,
        bool landmark)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);

        string normalizedPackage = packageName.ToUpperInvariant();
        return normalizedPackage switch
        {
            "BLDG" or "UBLD" when landmark => DistanceCullingClass.Landmark,
            "BLDG" or "UBLD" => DistanceCullingClass.Building,
            "FRN" when lodLevel == 2 => DistanceCullingClass.FurnitureLod2,
            "FRN" when lodLevel == 3 => DistanceCullingClass.FurnitureLod3,
            "BRID" when lodLevel == 2 => DistanceCullingClass.BridgeLod2,
            "TRAN" when lodLevel == 3 => DistanceCullingClass.TransportationLod3,
            "VEG" when lodLevel == 2 => DistanceCullingClass.VegetationLod2,
            "VEG" when lodLevel == 3 => DistanceCullingClass.VegetationLod3,
            _ => null,
        };
    }

    public static bool IsBuildingLandmark(ConstructionCityObjectDraft cityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        return IsBuildingPackage(cityObject.PackageName)
            && BuildingFacadeScale.Classify(
                cityObject.FloorsAboveGround,
                cityObject.MeasuredHeightMeters,
                cityObject.GeometryHeightMeters,
                cityObject.BuildingAttributes.BuildingFootprintArea?.Value).Landmark;
    }

    private static bool IsBuildingPackage(string packageName)
    {
        return string.Equals(packageName, "bldg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(packageName, "ubld", StringComparison.OrdinalIgnoreCase);
    }
}
