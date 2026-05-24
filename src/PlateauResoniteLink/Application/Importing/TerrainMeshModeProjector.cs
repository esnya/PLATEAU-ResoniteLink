using System;
using System.Collections.Generic;
using System.Threading;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class TerrainMeshModeProjector
{
    public static ImportedCityObject Project(
        ParsedCityObject cityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        PlateauImportRequest request,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        IDefaultMaterialResolver materialResolver,
        CityObjectProjection projectCityObject,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(globalOriginPoint);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(requestedMeshAreas);
        ArgumentNullException.ThrowIfNull(materialResolver);
        ArgumentNullException.ThrowIfNull(projectCityObject);

        bool isDem = string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase);
        if (!isDem || request.TerrainMeshMode == TerrainMeshMode.Static)
        {
            return projectCityObject(cityObject, globalOriginPoint, globalCartesian, demTerrainTextureOverlay, materialResolver);
        }

        bool hasGrid = DemTerrainGridProjector.TryProject(
            cityObject,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            request,
            requestedMeshAreas,
            materialResolver,
            progressReporter,
            cancellationToken,
            out ImportedCityObject? heightMapCityObject);
        if (!hasGrid)
        {
            return request.TerrainMeshMode == TerrainMeshMode.Dynamic
                ? CreateNonRenderableCityObject(cityObject)
                : projectCityObject(cityObject, globalOriginPoint, globalCartesian, demTerrainTextureOverlay, materialResolver);
        }

        if (request.TerrainMeshMode == TerrainMeshMode.Grid)
        {
            return heightMapCityObject!;
        }

        ImportedCityObject staticCityObject = projectCityObject(
            cityObject,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            materialResolver);
        TriangleMeshGeometry staticMesh = AssertTriangleMeshGeometry(staticCityObject);
        TriangleMeshGeometry rebasedStaticMesh = DemTerrainGridChunkBoundaryAligner.RebaseTriangleMeshToTransform(
            staticMesh,
            staticCityObject.Transform,
            heightMapCityObject!.Transform);
        return heightMapCityObject with
        {
            Geometry = new DynamicTerrainGeometry(rebasedStaticMesh, AssertTerrainGridGeometry(heightMapCityObject)),
            Materials = staticCityObject.Materials,
        };
    }

    private static ImportedCityObject CreateNonRenderableCityObject(ParsedCityObject cityObject)
    {
        return new ImportedCityObject(
            cityObject.SlotKey,
            cityObject.DisplayName,
            cityObject.PackageName,
            cityObject.ActualMeshCode,
            cityObject.LodLevel,
            new Transform3D(new Float3(0.0, 0.0, 0.0)),
            new TriangleMeshGeometry(new ImportedMesh([], [])),
            [],
            SourceFileRelativePath: cityObject.SourceFileRelativePath);
    }

    private static TriangleMeshGeometry AssertTriangleMeshGeometry(ImportedCityObject cityObject)
    {
        return cityObject.Geometry as TriangleMeshGeometry
            ?? throw new InvalidOperationException("Dynamic terrain static variant must be projected as a triangle mesh.");
    }

    private static TerrainGridGeometry AssertTerrainGridGeometry(ImportedCityObject cityObject)
    {
        return cityObject.Geometry as TerrainGridGeometry
            ?? throw new InvalidOperationException("Dynamic terrain grid variant must be projected as a terrain grid.");
    }
}
