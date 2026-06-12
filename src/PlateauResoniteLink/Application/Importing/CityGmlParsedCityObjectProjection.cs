using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.Extensions.Logging;

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
        IReadOnlyList<string> selectedMeshCodes,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver,
        Func<ParsedCityObject, bool>? predicate = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFile);
        ArgumentNullException.ThrowIfNull(referenceSystem);
        ArgumentNullException.ThrowIfNull(globalOriginPoint);
        ArgumentNullException.ThrowIfNull(selectedMeshCodes);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(materialResolver);

        LocalCityGmlObjectProjection.ValidateCompatibleReferenceSystem(
            referenceSystem,
            sourceFile.ReferenceSystem);

        ParsedCityObject[] projectedInputCityObjects =
            DemCityObjectAggregation.AggregateBySourceFileAndThirdMesh(
                sourceFile.SourceFile,
                sourceFile.CityObjects,
                selectedMeshCodes);

        if (string.Equals(sourceFile.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            && request.TerrainMeshMode is TerrainMeshMode.Grid or TerrainMeshMode.Dynamic)
        {
            ConstructionCityObjectDraft? sourceFileTerrainGridSamplingDraft = CreateDemSourceFileTerrainGridSamplingDraft(sourceFile);
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
                             logger,
                             demTerrainGridSamplingSourceOverride: sourceFileTerrainGridSamplingDraft,
                             cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return AttachSourceFileRoot(cityObject, sourceFile.SourceFile);
                }
            }

            yield break;
        }

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
                         logger,
                         cancellationToken: cancellationToken))
            {
                yield return AttachSourceFileRoot(cityObject, sourceFile.SourceFile);
            }
        }
    }

    private static ImportedCityObject AttachSourceFileRoot(
        ImportedCityObject cityObject,
        SourceFileDescriptor sourceFile)
    {
        return cityObject with
        {
            SourceFileRootMeshCode = sourceFile.EffectiveSourceFileRootMeshCode,
        };
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
        ILogger? logger = null,
        ConstructionCityObjectDraft? demTerrainGridSamplingSourceOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsedCityObject);
        ArgumentNullException.ThrowIfNull(globalOriginPoint);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(materialResolver);

        double? geometryHeightMeters = ResolveGeometryHeightMeters(parsedCityObject.Surfaces);
        ParsedCityObject terrainAlignedParsedCityObject = ConformCityObjectToTerrain(parsedCityObject, terrainHeightSampler) with
        {
            GeometryHeightMeters = geometryHeightMeters,
        };
        ConstructionCityObjectDraft constructionDraft = GeneratedLod1RoofCityObjectFactory.CreateDraft(terrainAlignedParsedCityObject);
        ConstructionCityObjectDraft terrainGridSamplingDraft = demTerrainGridSamplingSourceOverride ?? constructionDraft;
        List<ImportedCityObject> projectedCityObjects = [];
        List<ImportedCityObject> generatedRoadMarkings = [];

        foreach ((ConstructionCityObjectDraft CityObject, TerrainTextureOverlay? Overlay) partitionedCityObject
                 in TerrainOverlayMaterialSourcePartitioner.PartitionConstructionCityObject(
                     constructionDraft,
                     demTerrainTextureOverlays,
                     requestedMeshCodeBounds,
                     AllowMissingGeneratedDemOverlayCoverage(constructionDraft),
                     logger,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportedCityObject cityObject = ProjectTerrainMeshModeCityObject(
                partitionedCityObject.CityObject,
                terrainGridSamplingDraft,
                globalOriginPoint,
                globalCartesian,
                partitionedCityObject.Overlay,
                request,
                requestedMeshCodeBounds,
                materialResolver,
                logger,
                cancellationToken);

            if (HasRenderableGeometry(cityObject))
            {
                projectedCityObjects.Add(cityObject);
            }

            GeodeticPoint markingOrigin = ResolveCityObjectOrigin(partitionedCityObject.CityObject);
            LocalCartesian? markingCartesian = partitionedCityObject.CityObject.ReferenceSystem.IsGeographic
                ? new LocalCartesian(
                    markingOrigin.Latitude,
                    markingOrigin.Longitude,
                    markingOrigin.Altitude,
                    partitionedCityObject.CityObject.ReferenceSystem.Geocentric)
                : null;
            ConstructionCityObjectDraft? roadMarkingCityObject = GeneratedRoadMarkingCityObjectFactory.Create(
                partitionedCityObject.CityObject,
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
                partitionedCityObject.Overlay,
                materialResolver) with
            {
                CollisionEnabled = false,
            };
            if (HasRenderableGeometry(markingObject))
            {
                generatedRoadMarkings.Add(markingObject);
            }
        }

        foreach (ImportedCityObject cityObject in projectedCityObjects)
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

    private static ConstructionCityObjectDraft? CreateDemSourceFileTerrainGridSamplingDraft(CachedSourceFileDescriptor sourceFile)
    {
        ParsedCityObject[] sourceObjects = sourceFile.CityObjects;
        if (sourceObjects.Length == 0)
        {
            return null;
        }

        ParsedSurface[] surfaces = sourceObjects
            .SelectMany(static cityObject => cityObject.Surfaces)
            .OrderBy(static surface => surface, ParsedSurfaceStructuralComparer.Instance)
            .ToArray();
        if (surfaces.Length == 0)
        {
            return null;
        }

        ParsedCityObject first = sourceObjects[0];
        ParsedCityObject samplingSource = first with
        {
            SlotKey = $"dem_source_file_sampling_{sourceFile.SourceFile.MatchedMeshCode}",
            DisplayName = $"DEM source file sampling {sourceFile.SourceFile.MatchedMeshCode}",
            ActualMeshCode = sourceFile.SourceFile.MatchedMeshCode,
            LodLevel = sourceObjects
                .Select(static cityObject => cityObject.LodLevel)
                .Where(static lodLevel => lodLevel.HasValue)
                .DefaultIfEmpty()
                .Max(),
            Surfaces = surfaces,
            SourceFileRelativePath = sourceFile.SourceFile.RelativePath,
            SharedAcrossMeshCodes = true,
            TerrainAligned = sourceObjects.Any(static cityObject => cityObject.TerrainAligned),
            GeodeticOriginOverride = null,
            FloorsAboveGround = null,
            MeasuredHeightMeters = null,
            GeometryHeightMeters = null,
        };
        return ConstructionCityObjectDraft.FromParsedCityObject(samplingSource);
    }

    internal static IEnumerable<MaterialBinding> EnumerateCommonMaterials(
        ParsedCityObject parsedCityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        ProjectionTerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver)
    {
        ArgumentNullException.ThrowIfNull(parsedCityObject);
        ArgumentNullException.ThrowIfNull(globalOriginPoint);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(materialResolver);

        double? geometryHeightMeters = ResolveGeometryHeightMeters(parsedCityObject.Surfaces);
        ParsedCityObject terrainAlignedParsedCityObject = ConformCityObjectToTerrain(parsedCityObject, terrainHeightSampler) with
        {
            GeometryHeightMeters = geometryHeightMeters,
        };
        ConstructionCityObjectDraft constructionDraft = GeneratedLod1RoofCityObjectFactory.CreateDraft(terrainAlignedParsedCityObject);

        foreach ((ConstructionCityObjectDraft CityObject, TerrainTextureOverlay? Overlay) partitionedCityObject
                 in TerrainOverlayMaterialSourcePartitioner.PartitionConstructionCityObject(
                     constructionDraft,
                     demTerrainTextureOverlays,
                     requestedMeshCodeBounds,
                     AllowMissingGeneratedDemOverlayCoverage(constructionDraft)))
        {
            GeodeticPoint cityObjectOrigin = ResolveCityObjectOrigin(partitionedCityObject.CityObject);
            LocalCartesian? cityObjectCartesian = partitionedCityObject.CityObject.ReferenceSystem.IsGeographic
                ? new LocalCartesian(
                    cityObjectOrigin.Latitude,
                    cityObjectOrigin.Longitude,
                    cityObjectOrigin.Altitude,
                    partitionedCityObject.CityObject.ReferenceSystem.Geocentric)
                : null;

            foreach (MaterialBinding material in CityGmlSurfaceMaterialResolver.CreateSharedCommonMaterialBindings(
                         partitionedCityObject.CityObject,
                         cityObjectOrigin,
                         cityObjectCartesian,
                         partitionedCityObject.Overlay,
                         materialResolver))
            {
                yield return material;
            }
        }
    }

    private static bool AllowMissingGeneratedDemOverlayCoverage(
        ConstructionCityObjectDraft parsedCityObject)
    {
        return string.Equals(parsedCityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase);
    }

    internal static ImportedCityObject ProjectTerrainMeshModeCityObject(
        ConstructionCityObjectDraft cityObject,
        ConstructionCityObjectDraft? demTerrainGridSamplingSource,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        PlateauImportRequest request,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        IDefaultMaterialResolver materialResolver,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        bool isDem = string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase);
        if (!isDem)
        {
            return CityGmlTriangleMeshCityObjectProjection.Project(cityObject, globalOriginPoint, globalCartesian, demTerrainTextureOverlay, materialResolver);
        }

        if (request.TerrainMeshMode == TerrainMeshMode.Static)
        {
            GeodeticPoint staticDemObjectOrigin = DemTerrainObjectFrameResolver.ResolveRequiredThirdMeshOrigin(
                cityObject.ActualMeshCode,
                cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
            return CityGmlTriangleMeshCityObjectProjection.ProjectTriangleMesh(
                cityObject,
                globalOriginPoint,
                globalCartesian,
                demTerrainTextureOverlay,
                materialResolver,
                staticDemObjectOrigin).CityObject;
        }

        bool hasGrid = CityGmlDemTerrainGridCityObjectProjection.TryProject(
            cityObject,
            demTerrainGridSamplingSource ?? cityObject,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            request,
            requestedMeshCodeBounds,
            materialResolver,
            logger,
            cancellationToken,
            out bool outsideTerrainGridSamplingBounds,
            out TerrainGridProjectedCityObject? heightMapCityObject);
        if (!hasGrid)
        {
            if (outsideTerrainGridSamplingBounds)
            {
                return CreateNonRenderableCityObject(cityObject);
            }

            return request.TerrainMeshMode == TerrainMeshMode.Dynamic
                ? CreateNonRenderableCityObject(cityObject)
                : CityGmlTriangleMeshCityObjectProjection.Project(cityObject, globalOriginPoint, globalCartesian, demTerrainTextureOverlay, materialResolver);
        }

        if (request.TerrainMeshMode == TerrainMeshMode.Grid)
        {
            return heightMapCityObject!.CityObject;
        }

        TerrainGridProjectedCityObject projectedHeightMap = heightMapCityObject!;
        GeodeticPoint dynamicDemObjectOrigin = DemTerrainObjectFrameResolver.ResolveRequiredThirdMeshOrigin(
            cityObject.ActualMeshCode,
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
        TriangleMeshProjectedCityObject staticCityObject = CityGmlTriangleMeshCityObjectProjection.ProjectTriangleMesh(
            cityObject,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            materialResolver,
            dynamicDemObjectOrigin);
        TriangleMeshGeometry rebasedStaticMesh = TriangleMeshTransformRebaser.Rebase(
            staticCityObject.Geometry,
            staticCityObject.CityObject.Transform,
            projectedHeightMap.CityObject.Transform);
        return projectedHeightMap.CityObject with
        {
            Geometry = new DynamicTerrainGeometry(rebasedStaticMesh, projectedHeightMap.Geometry),
            Materials = staticCityObject.CityObject.Materials,
        };
    }

    private static ImportedCityObject CreateNonRenderableCityObject(ConstructionCityObjectDraft cityObject)
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
        if (surface.InteriorRings.Length != 0)
        {
            return [surface];
        }

        if (!ShouldSubdivideTerrainAlignedCityObject(cityObject.PackageName, cityObject.LodLevel))
        {
            return [surface];
        }

        if (surface.ExteriorRing.Vertices.Length != 4)
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
        if (!RoadSurfaceQuad.TryCreate(surface.ExteriorRing, positions, out RoadSurfaceQuad quad))
        {
            return [surface];
        }

        if (!IsNearHorizontalSurface(positions))
        {
            return [surface];
        }

        EdgePairSelection edgePair = RoadSurfaceEdgePairSelector.Select(quad);
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

    private static GeodeticPoint ResolveCityObjectOrigin(ConstructionCityObjectDraft cityObject)
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
