using System;
using System.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class ParsedCityObjectTerrainPreparation
{
    public static ParsedCityObject Prepare(
        ParsedCityObject cityObject,
        TerrainHeightSampler? terrainHeightSampler)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        double? geometryHeightMeters = CityObjectGeometryMetrics.TryGetGeometryHeightMeters(
            cityObject.Surfaces.Select(static surface => surface));
        return Lod1RoofGenerator.Apply(TerrainSurfaceConformer.Conform(cityObject, terrainHeightSampler)) with
        {
            GeometryHeightMeters = geometryHeightMeters,
        };
    }
}
