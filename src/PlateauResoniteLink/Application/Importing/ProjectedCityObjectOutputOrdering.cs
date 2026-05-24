using System;
using System.Collections.Generic;
using System.Threading;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class ProjectedCityObjectOutputOrdering
{
    public static IEnumerable<ImportedCityObject> CreateOrderedOutput(
        ParsedCityObject sourceCityObject,
        IReadOnlyList<ImportedCityObject> projectedCityObjects,
        IReadOnlyList<ImportedCityObject> generatedRoadMarkings,
        TerrainMeshMode terrainMeshMode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceCityObject);
        ArgumentNullException.ThrowIfNull(projectedCityObjects);
        ArgumentNullException.ThrowIfNull(generatedRoadMarkings);

        ImportedCityObject[] alignedCityObjects =
            terrainMeshMode is TerrainMeshMode.Grid or TerrainMeshMode.Dynamic
            && string.Equals(sourceCityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
                ? DemTerrainGridChunkBoundaryAligner.AlignAdjacentBoundaries(projectedCityObjects)
                : [.. projectedCityObjects];

        foreach (ImportedCityObject cityObject in alignedCityObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return cityObject;
        }

        foreach (ImportedCityObject markingObject in generatedRoadMarkings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return markingObject;
        }
    }
}
