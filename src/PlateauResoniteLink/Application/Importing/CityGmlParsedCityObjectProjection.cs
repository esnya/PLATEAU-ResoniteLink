using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using PlateauResoniteLink.Domain.Importing;

using LocalCartesian = GeographicLib.LocalCartesian;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlParsedCityObjectProjection
{
    internal static IEnumerable<ImportedCityObject> ProjectSourceFile(
        CachedSourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver,
        Func<ParsedCityObject, bool>? predicate = null,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFile);
        ArgumentNullException.ThrowIfNull(referenceSystem);
        ArgumentNullException.ThrowIfNull(globalOriginPoint);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(materialResolver);

        LocalCityGmlObjectProjection.ValidateCompatibleReferenceSystem(
            referenceSystem,
            sourceFile.CityObjects.FirstOrDefault()?.ReferenceSystem ?? referenceSystem);

        ParsedCityObject[] projectedInputCityObjects =
            DemCityObjectAggregation.AggregateBySourceFileAndThirdMesh(
                sourceFile.SourceFile,
                sourceFile.CityObjects);

        foreach (ParsedCityObject parsedCityObject in projectedInputCityObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predicate is not null && !predicate(parsedCityObject))
            {
                continue;
            }

            foreach (ImportedCityObject cityObject in Project(
                         parsedCityObject,
                         globalOriginPoint,
                         globalCartesian,
                         demTerrainTextureOverlays,
                         requestedMeshCodeBounds,
                         terrainHeightSampler: null,
                         request,
                         materialResolver,
                         progressReporter,
                         cancellationToken))
            {
                yield return cityObject;
            }
        }
    }

    internal static IEnumerable<ImportedCityObject> Project(
        ParsedCityObject parsedCityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        ProjectionTerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsedCityObject);
        ArgumentNullException.ThrowIfNull(globalOriginPoint);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(materialResolver);

        double? geometryHeightMeters = ResolveGeometryHeightMeters(parsedCityObject.Surfaces);
        ParsedCityObject terrainAlignedParsedCityObject =
            GeneratedLod1RoofCityObjectFactory.Create(ConformCityObjectToTerrain(parsedCityObject, terrainHeightSampler)) with
            {
                GeometryHeightMeters = geometryHeightMeters,
            };
        List<ImportedCityObject> projectedCityObjects = [];
        List<ImportedCityObject> generatedRoadMarkings = [];

        foreach ((ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject
                 in TerrainOverlayProjectionSplitPolicy.SplitParsedCityObject(
                     terrainAlignedParsedCityObject,
                     demTerrainTextureOverlays,
                     requestedMeshCodeBounds,
                     progressReporter,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TerrainOverlayProjectionSplitPolicy.ShouldProjectSplit(
                    splitCityObject.CityObject.ActualMeshCode,
                    request.MeshCode,
                    requestedMeshCodeBounds,
                    splitCityObject.Overlay))
            {
                throw CreateTerrainOverlayMeshCodeMismatchException(
                    "project",
                    splitCityObject.CityObject.ActualMeshCode,
                    request.MeshCode,
                    requestedMeshCodeBounds,
                    splitCityObject.Overlay);
            }

            ImportedCityObject cityObject = ProjectTerrainMeshModeCityObject(
                splitCityObject.CityObject,
                globalOriginPoint,
                globalCartesian,
                splitCityObject.Overlay,
                request,
                requestedMeshCodeBounds,
                materialResolver,
                progressReporter,
                cancellationToken);

            if (HasRenderableGeometry(cityObject))
            {
                projectedCityObjects.Add(cityObject);
            }

            GeodeticPoint markingOrigin = ResolveCityObjectOrigin(splitCityObject.CityObject);
            LocalCartesian? markingCartesian = splitCityObject.CityObject.ReferenceSystem.IsGeographic
                ? new LocalCartesian(
                    markingOrigin.Latitude,
                    markingOrigin.Longitude,
                    markingOrigin.Altitude,
                    splitCityObject.CityObject.ReferenceSystem.Geocentric)
                : null;
            ParsedCityObject? roadMarkingCityObject = GeneratedRoadMarkingCityObjectFactory.Create(
                splitCityObject.CityObject,
                markingOrigin,
                markingCartesian);
            if (roadMarkingCityObject is null)
            {
                continue;
            }

            ImportedCityObject markingObject = CityGmlTriangleMeshCityObjectProjection.Project(
                roadMarkingCityObject,
                globalOriginPoint,
                globalCartesian,
                splitCityObject.Overlay,
                materialResolver) with
            {
                CollisionEnabled = false,
            };
            if (HasRenderableGeometry(markingObject))
            {
                generatedRoadMarkings.Add(markingObject);
            }
        }

        ImportedCityObject[] alignedCityObjects =
            request.TerrainMeshMode is TerrainMeshMode.Grid or TerrainMeshMode.Dynamic
            && string.Equals(terrainAlignedParsedCityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
                ? DemTerrainGridChunkBoundaryAlignmentPolicy.Align(projectedCityObjects)
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

    internal static IEnumerable<MaterialBinding> EnumerateCommonMaterials(
        ParsedCityObject parsedCityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        ProjectionTerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver)
    {
        ArgumentNullException.ThrowIfNull(parsedCityObject);
        ArgumentNullException.ThrowIfNull(globalOriginPoint);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(materialResolver);

        double? geometryHeightMeters = ResolveGeometryHeightMeters(parsedCityObject.Surfaces);
        ParsedCityObject terrainAlignedParsedCityObject =
            GeneratedLod1RoofCityObjectFactory.Create(ConformCityObjectToTerrain(parsedCityObject, terrainHeightSampler)) with
            {
                GeometryHeightMeters = geometryHeightMeters,
            };

        foreach ((ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject
                 in TerrainOverlayProjectionSplitPolicy.SplitParsedCityObject(
                     terrainAlignedParsedCityObject,
                     demTerrainTextureOverlays,
                     requestedMeshCodeBounds))
        {
            if (!TerrainOverlayProjectionSplitPolicy.ShouldProjectSplit(
                    splitCityObject.CityObject.ActualMeshCode,
                    request.MeshCode,
                    requestedMeshCodeBounds ?? [],
                    splitCityObject.Overlay))
            {
                throw CreateTerrainOverlayMeshCodeMismatchException(
                    "common-material-enumeration",
                    splitCityObject.CityObject.ActualMeshCode,
                    request.MeshCode,
                    requestedMeshCodeBounds,
                    splitCityObject.Overlay);
            }

            GeodeticPoint cityObjectOrigin = ResolveCityObjectOrigin(splitCityObject.CityObject);
            LocalCartesian? cityObjectCartesian = splitCityObject.CityObject.ReferenceSystem.IsGeographic
                ? new LocalCartesian(
                    cityObjectOrigin.Latitude,
                    cityObjectOrigin.Longitude,
                    cityObjectOrigin.Altitude,
                    splitCityObject.CityObject.ReferenceSystem.Geocentric)
                : null;
            ConstructionCityObjectDraft draft = ConstructionCityObjectDraft.FromParsedCityObject(splitCityObject.CityObject);

            foreach (MaterialBinding material in request.TerrainMeshMode is TerrainMeshMode.Grid or TerrainMeshMode.Dynamic
                         && string.Equals(splitCityObject.CityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
                            ? CityGmlSurfaceMaterialResolver.CreateDemTerrainGridMaterials(
                                draft,
                                cityObjectOrigin,
                                cityObjectCartesian,
                                splitCityObject.Overlay,
                                request.MeshCode,
                                requestedMeshCodeBounds,
                                materialResolver)
                            : CityGmlSurfaceMaterialResolver.CreateSharedCommonMaterialBindings(
                                draft,
                                cityObjectOrigin,
                                cityObjectCartesian,
                                splitCityObject.Overlay,
                                materialResolver))
            {
                yield return material;
            }
        }
    }

    internal static ImportedCityObject ProjectTerrainMeshModeCityObject(
        ParsedCityObject cityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        PlateauImportRequest request,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        IDefaultMaterialResolver materialResolver,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        bool isDem = string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase);
        if (!isDem || request.TerrainMeshMode == TerrainMeshMode.Static)
        {
            return CityGmlTriangleMeshCityObjectProjection.Project(cityObject, globalOriginPoint, globalCartesian, demTerrainTextureOverlay, materialResolver);
        }

        bool hasGrid = CityGmlDemTerrainGridCityObjectProjection.TryProject(
            cityObject,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            request,
            requestedMeshCodeBounds,
            materialResolver,
            progressReporter,
            cancellationToken,
            out ImportedCityObject? heightMapCityObject);
        if (!hasGrid)
        {
            return request.TerrainMeshMode == TerrainMeshMode.Dynamic
                ? CreateNonRenderableCityObject(cityObject)
                : CityGmlTriangleMeshCityObjectProjection.Project(cityObject, globalOriginPoint, globalCartesian, demTerrainTextureOverlay, materialResolver);
        }

        if (request.TerrainMeshMode == TerrainMeshMode.Grid)
        {
            return heightMapCityObject!;
        }

        ImportedCityObject staticCityObject = CityGmlTriangleMeshCityObjectProjection.Project(
            cityObject,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            materialResolver);
        TriangleMeshGeometry staticMesh = AssertTriangleMeshGeometry(staticCityObject);
        TriangleMeshGeometry rebasedStaticMesh = TriangleMeshTransformRebaser.Rebase(
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

    private static ParsedCityObject ConformCityObjectToTerrain(
        ParsedCityObject parsedCityObject,
        ProjectionTerrainHeightSampler? terrainHeightSampler)
    {
        if (terrainHeightSampler is null
            || !CityGmlTerrainConformer.ShouldTerrainAlign(parsedCityObject.PackageName, parsedCityObject.LodLevel))
        {
            return parsedCityObject;
        }

        ParsedCityObject subdividedCityObject = SubdivideTerrainAlignedCityObject(parsedCityObject);
        GeodeticPoint cityObjectOrigin = ResolveCityObjectOrigin(subdividedCityObject);
        LocalCartesian? cityObjectCartesian = subdividedCityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                subdividedCityObject.ReferenceSystem.Geocentric)
            : null;

        TerrainConformanceResult conformance = CityGmlTerrainConformer.Conform(
            subdividedCityObject,
            terrainHeightSampler,
            cityObjectOrigin,
            cityObjectCartesian);

        return conformance.TerrainAligned
            ? subdividedCityObject with
            {
                Surfaces = conformance.Surfaces,
                TerrainAligned = true,
            }
            : subdividedCityObject;
    }

    private static ParsedCityObject SubdivideTerrainAlignedCityObject(ParsedCityObject cityObject)
    {
        if (!ShouldSubdivideTerrainAlignedCityObject(cityObject.PackageName, cityObject.LodLevel))
        {
            return cityObject;
        }

        List<ParsedSurface> subdividedSurfaces = [];
        foreach (ParsedSurface surface in cityObject.Surfaces)
        {
            subdividedSurfaces.AddRange(SubdivideTransportationSurfaceForTerrainAlignment(surface, cityObject));
        }

        return subdividedSurfaces.Count == cityObject.Surfaces.Length
            ? cityObject
            : cityObject with { Surfaces = subdividedSurfaces.ToArray() };
    }

    private static List<ParsedSurface> SubdivideTransportationSurfaceForTerrainAlignment(
        ParsedSurface surface,
        ParsedCityObject cityObject)
    {
        if (surface.InteriorRings.Length != 0
            || surface.ExteriorRing.Vertices.Length != 4)
        {
            return [surface];
        }

        if (!ShouldSubdivideTerrainAlignedCityObject(cityObject.PackageName, cityObject.LodLevel))
        {
            return [surface];
        }

        GeodeticPoint cityObjectOrigin = ResolveCityObjectOrigin(cityObject);
        LocalCartesian? cityObjectCartesian = cityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObject.ReferenceSystem.Geocentric)
            : null;
        Float3[] positions = surface.ExteriorRing.Vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        if (!IsNearHorizontalSurface(positions))
        {
            return [surface];
        }

        EdgePairSelection edgePair = RoadSurfaceEdgePairSelector.Select(surface.ExteriorRing, positions);
        return TerrainAlignedTransportationSurfaceSplitter.Split(surface, positions, edgePair);
    }

    private static bool ShouldSubdivideTerrainAlignedCityObject(string packageName, int? lodLevel)
    {
        return PlateauPackageCatalog.IsRoadPackage(packageName)
            && (!lodLevel.HasValue || lodLevel.Value < 3);
    }

    private static GeodeticPoint ResolveCityObjectOrigin(ParsedCityObject cityObject)
    {
        return CityObjectOriginResolver.Resolve(
            cityObject.GeodeticOriginOverride,
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
    }

    private static double? ResolveGeometryHeightMeters(IEnumerable<ParsedSurface> surfaces)
    {
        return CityObjectAltitudeMetricsResolver.TryGetGeometryHeightMeters(
            surfaces.SelectMany(static surface => surface.Vertices));
    }

    private static bool IsNearHorizontalSurface(Float3[] positions)
    {
        Float3? normal = ComputePolygonNormal(positions);
        return normal is not null && Math.Abs(normal.Y) >= 0.7;
    }

    private static Float3? ComputePolygonNormal(Float3[] positions)
    {
        if (positions.Length < 3)
        {
            return null;
        }

        double normalX = 0.0;
        double normalY = 0.0;
        double normalZ = 0.0;
        for (int index = 0; index < positions.Length; index++)
        {
            Float3 current = positions[index];
            Float3 next = positions[(index + 1) % positions.Length];
            normalX += (current.Y - next.Y) * (current.Z + next.Z);
            normalY += (current.Z - next.Z) * (current.X + next.X);
            normalZ += (current.X - next.X) * (current.Y + next.Y);
        }

        double length = Math.Sqrt((normalX * normalX) + (normalY * normalY) + (normalZ * normalZ));
        if (length <= 1e-8)
        {
            return null;
        }

        return new Float3(normalX / length, normalY / length, normalZ / length);
    }

    private static Float3 CreateScenePosition(
        GeodeticPoint point,
        GeodeticPoint origin,
        LocalCartesian? cartesian)
    {
        return SceneAxisMapper.CreatePosition(
            point.Latitude,
            point.Longitude,
            point.Altitude,
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            cartesian);
    }

    private static InvalidOperationException CreateTerrainOverlayMeshCodeMismatchException(
        string phase,
        string actualMeshCode,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        TerrainTextureOverlay? terrainOverlay)
    {
        return TerrainOverlayDiagnostics.CreateMeshCodeMismatchException(
            phase,
            actualMeshCode,
            requestedMeshCode,
            requestedMeshCodeBounds,
            terrainOverlay);
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

    private static bool HasRenderableGeometry(ImportedCityObject cityObject)
    {
        return cityObject.Geometry switch
        {
            TriangleMeshGeometry triangleMesh => triangleMesh.Mesh.Submeshes.Count > 0,
            TerrainGridGeometry heightMap => heightMap.Width > 1 && heightMap.Height > 1,
            DynamicTerrainGeometry dynamicTerrain =>
                dynamicTerrain.StaticMesh.Mesh.Submeshes.Count > 0
                && dynamicTerrain.GridMesh.Width > 1
                && dynamicTerrain.GridMesh.Height > 1,
            _ => false,
        };
    }
}
