using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;

using PlateauResoniteLink.Domain.Importing;

using LocalCartesian = GeographicLib.LocalCartesian;

namespace PlateauResoniteLink.Application.Importing;

internal static class LocalCityGmlObjectProjection
{
    internal const double BuildingBottomCullBandMeters = 0.1;
    public const string DefaultDemTerrainTexturePath = DemTerrainTextureDefaults.PlateauOrthoPath;
    public const string DefaultDemTerrainTextureUrlTemplate = DemTerrainTextureDefaults.PlateauOrthoUrlTemplate;
    public const string DefaultDemTerrainTextureFallbackUrlTemplate = DemTerrainTextureDefaults.GsiFallbackUrlTemplate;
    public const int DefaultDemTerrainTextureZoomLevel = DemTerrainTextureDefaults.PlateauOrthoZoomLevel;
    public const int DefaultDemTerrainTextureFallbackZoomLevel = DemTerrainTextureDefaults.FallbackZoomLevel;
    public const int DefaultDemTerrainTextureMaxSize = DemTerrainTextureDefaults.MaxTextureSize;
    public static readonly MaterialDepthOffset DefaultTerrainAlignedMaterialDepthOffset = new(-10.0, -10.0);

    private static readonly XNamespace App = "http://www.opengis.net/citygml/appearance/2.0";
    private static readonly XNamespace Core = "http://www.opengis.net/citygml/2.0";
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";

    internal static global::PlateauResoniteLink.Application.Importing.ParsedCityObject? ParseCityObject(
        XElement cityObjectElement,
        string packageName,
        string relativeSourceFile,
        string actualMeshCode,
        bool sharedAcrossMeshCodes,
        ICityGmlAppearanceStore appearanceStore,
        ICityGmlLodSelector lodSelector,
        ProjectionCoordinateReferenceSystem coordinateReferenceSystem,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        LodFilteringStrategy lodFilteringStrategy)
    {
        string objectTypeName = cityObjectElement.Name.LocalName;
        string objectId = GetAttribute(cityObjectElement, Gml + "id") ?? objectTypeName;
        string? displayName = cityObjectElement.Elements(Gml + "name").FirstOrDefault()?.Value.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = objectId;
        }

        string resolvedActualMeshCode =
            sharedAcrossMeshCodes && string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
                ? ConcreteMeshCodeResolver.ResolveActualMeshCode(displayName!, objectId, actualMeshCode)
                : actualMeshCode;
        BuildingAttributeContext buildingAttributes = BuildingAttributeParser.Parse(cityObjectElement);
        int? floorsAboveGround = BuildingMetricNormalizer.TryGetKnownPositiveInteger(buildingAttributes.StoreysAboveGround);
        double? measuredHeightMeters = BuildingMetricNormalizer.TryGetKnownPositiveMetric(buildingAttributes.MeasuredHeightMeters);

        bool isMarking = displayName.Contains("Marking", StringComparison.OrdinalIgnoreCase)
            || objectId.Contains("Marking", StringComparison.OrdinalIgnoreCase)
            || objectId.Contains("_road_marking", StringComparison.Ordinal);

        CityGmlLodSelection lodSelection = lodSelector.SelectPreferredSurfaceElements(
            cityObjectElement,
            packageName,
            isMarking,
            lodFilteringStrategy);
        XElement[] preferredSurfaceElements = lodSelection.SurfaceElements;
        int? lodLevel = lodSelection.LodLevel;

        if (!lodFilteringStrategy.ShouldIncludeByPattern(packageName, objectId, isMarking))
        {
            return null;
        }

        if (preferredSurfaceElements.Length == 0 && lodFilteringStrategy.ShouldExcludeLod(packageName, lodLevel, isMarking))
        {
            return null;
        }

        global::PlateauResoniteLink.Application.Importing.ParsedSurface[] surfaces = preferredSurfaceElements
            .Select(surfaceElement => CityGmlParsedSurfaceReader.Parse(surfaceElement, appearanceStore))
            .Where(static surface => surface is not null)
            .Select(static surface => surface!)
            .Select(surface => CityGmlParsedSurfaceReader.ApplyPackageDefaults(packageName, surface))
            .OrderBy(static surface => ParsedSurfaceStableSortKey.Create(surface), StringComparer.Ordinal)
            .ToArray();

        if (surfaces.Length == 0)
        {
            return null;
        }

        if (requestedMeshAreas is not null
            && requestedMeshAreas.Count > 0
            && coordinateReferenceSystem.IsGeographic)
        {
            bool intersectsRequestedMeshArea = sharedAcrossMeshCodes
                && TryCreateMeshCodeBounds(resolvedActualMeshCode, out MeshCodeBounds? resolvedActualMeshArea)
                    ? MeshCodeBoundsFilter.IntersectsRequestedAreas(resolvedActualMeshArea!, requestedMeshAreas)
                    : MeshCodeBoundsFilter.IntersectsRequestedAreas(
                        surfaces.SelectMany(static surface => surface.Vertices)
                            .Select(static point => (point.Latitude, point.Longitude)),
                        requestedMeshAreas);
            if (!intersectsRequestedMeshArea)
            {
                return null;
            }
        }

        string fileStem = Path.GetFileNameWithoutExtension(relativeSourceFile);
        string slotKey = SanitizeIdentifier($"{packageName}_{fileStem}_{objectId}");
        return new global::PlateauResoniteLink.Application.Importing.ParsedCityObject(
            slotKey,
            displayName!,
            packageName,
            resolvedActualMeshCode,
            lodLevel,
            surfaces,
            global::PlateauResoniteLink.Application.Importing.CoordinateReferenceSystem.FromProjectionModel(coordinateReferenceSystem),
            relativeSourceFile,
            SharedAcrossMeshCodes: sharedAcrossMeshCodes,
            FloorsAboveGround: floorsAboveGround,
            MeasuredHeightMeters: measuredHeightMeters,
            BuildingAttributes: buildingAttributes);
    }

    internal static TerrainTextureOverlay[] CreateDemTerrainTextureOverlays(
        MeshCodeBounds demBounds,
        IReadOnlyList<string> requestedMeshCodes)
    {
        return DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(
                DemTerrainBounds.FromProjectionModel(demBounds),
                requestedMeshCodes)
            .Select(static region => DemTerrainTextureDefaults.CreatePlateauOrthoWithGsiFallbackOverlay(region.GeographicBounds))
            .ToArray();
    }

    private static bool TryCreateMeshCodeBounds(string meshCode, out MeshCodeBounds? meshCodeArea)
    {
        meshCodeArea = MeshCodeBounds.TryParse(meshCode);
        return meshCodeArea is not null;
    }

    private static (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude) GetBounds(
        IEnumerable<ParsedCityObject> cityObjects)
    {
        List<GeodeticPoint> allPoints = cityObjects
            .SelectMany(static cityObject => cityObject.Surfaces)
            .SelectMany(static surface => surface.Vertices)
            .ToList();

        return (
            allPoints.Min(static point => point.Latitude),
            allPoints.Max(static point => point.Latitude),
            allPoints.Min(static point => point.Longitude),
            allPoints.Max(static point => point.Longitude),
            allPoints.Min(static point => point.Altitude));
    }

    internal static MeshCodeBounds? ResolveDemTerrainBounds(
        IEnumerable<global::PlateauResoniteLink.Application.Importing.ParsedSourceFileResult> demParsedSourceFiles,
        MeshCodeBounds? fallbackBounds)
    {
        DemTerrainBounds? bounds = DemSourceDiscoverySupport.ResolveDemTerrainBounds(
            demParsedSourceFiles,
            fallbackBounds is null ? null : DemTerrainBounds.FromProjectionModel(fallbackBounds));
        return bounds?.ToProjectionModel();
    }

    internal static ImportedCityObject ProjectCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        double? geometryHeightMeters = cityObject.GeometryHeightMeters
            ?? CityObjectGeometryMetrics.TryGetGeometryHeightMeters(cityObject.Surfaces);
        cityObject = Lod1RoofGenerator.Apply(cityObject) with
        {
            GeometryHeightMeters = geometryHeightMeters,
        };
        ProjectedCityObjectContext projectionContext = ProjectedCityObjectContextFactory.Create(
            cityObject,
            globalOriginPoint,
            globalCartesian);
        List<MeshVertex> vertices = [];
        List<MeshSubmesh> submeshes = [];
        List<MaterialBinding> materials = [];
        DemTerrainTextureUvProjection? demUvProjection = DemTerrainTextureUvProjection.TryCreate(cityObject.ActualMeshCode, demTerrainTextureOverlay);

        List<ResolvedSurfaceMaterial> resolvedSurfaces =
        [
            .. cityObject.Surfaces
                .Where(surface => !projectionContext.CulledSurfaceIds.Contains(surface.PolygonId))
                .Select(surface => SurfaceMaterialResolver.Resolve(
                    cityObject,
                    projectionContext.Origin,
                    projectionContext.Cartesian,
                    surface,
                    projectionContext.MinimumAltitude,
                    demTerrainTextureOverlay,
                    materialResolver)),
        ];

        SurfaceMaterialGroup[] materialGroups = SurfaceMaterialGrouping.Create(cityObject.ActualMeshCode, resolvedSurfaces);

        for (int materialIndex = 0; materialIndex < materialGroups.Length; materialIndex++)
        {
            SurfaceMaterialGroup materialGroup = materialGroups[materialIndex];
            List<int> indices = [];

            foreach (ResolvedSurfaceMaterial resolvedSurface in materialGroup.Surfaces)
            {
                SurfaceTriangulationProjector.Append(
                    new SurfaceTriangulationRequest(
                        cityObject.PackageName,
                        resolvedSurface.Surface,
                        resolvedSurface.Material,
                        projectionContext.Origin,
                        projectionContext.Cartesian,
                        projectionContext.FacadeUvProjectionContext,
                        demUvProjection),
                    vertices,
                    indices);
            }

            if (indices.Count == 0)
            {
                continue;
            }

            submeshes.Add(new MeshSubmesh(materialIndex, indices));
            materials.Add(materialGroup.Binding);
        }

        return new ImportedCityObject(
            ObjectKey: cityObject.SlotKey,
            DisplayName: cityObject.DisplayName,
            PackageName: cityObject.PackageName,
            ActualMeshCode: cityObject.ActualMeshCode,
            LodLevel: cityObject.LodLevel,
            Transform: new Transform3D(ToContractFloat3(projectionContext.SlotPosition)),
            Mesh: new ImportedMesh(vertices.ToArray(), submeshes.ToArray()),
            Materials: materials,
            SourceFileRelativePath: cityObject.SourceFileRelativePath);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string? GetAttribute(XElement element, XName attributeName)
    {
        return element.Attribute(attributeName)?.Value;
    }

    private static string SanitizeIdentifier(string value)
    {
        return string.Concat(
            value.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
    }

    internal static IEnumerable<ImportedCityObject> ProjectCityObjects(
        global::PlateauResoniteLink.Application.Importing.CachedSourceFileDescriptor sourceFile,
        global::PlateauResoniteLink.Application.Importing.CoordinateReferenceSystem referenceSystem,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver,
        Func<global::PlateauResoniteLink.Application.Importing.ParsedCityObject, bool>? predicate = null,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFile);
        ArgumentNullException.ThrowIfNull(referenceSystem);
        ArgumentNullException.ThrowIfNull(globalOriginPoint);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(materialResolver);

        ProjectionCoordinateReferenceSystem projectionReferenceSystem = referenceSystem.ToProjectionModel();
        ValidateCompatibleReferenceSystem(
            projectionReferenceSystem,
            sourceFile.CityObjects.FirstOrDefault()?.ReferenceSystem.ToProjectionModel() ?? projectionReferenceSystem);

        global::PlateauResoniteLink.Application.Importing.ParsedCityObject[] projectedInputCityObjects =
            global::PlateauResoniteLink.Application.Importing.DemCityObjectAggregation.AggregateBySourceFileAndThirdMesh(
                sourceFile.SourceFile,
                sourceFile.CityObjects);

        foreach (global::PlateauResoniteLink.Application.Importing.ParsedCityObject parsedCityObject in projectedInputCityObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predicate is not null && !predicate(parsedCityObject))
            {
                continue;
            }

            foreach (ImportedCityObject cityObject in ProjectParsedCityObject(
                         parsedCityObject,
                         globalOriginPoint,
                         globalCartesian,
                         demTerrainTextureOverlays,
                         requestedMeshAreas,
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

    internal static IEnumerable<ImportedCityObject> ProjectParsedCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject parsedCityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsedCityObject);
        ArgumentNullException.ThrowIfNull(globalOriginPoint);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(materialResolver);

        global::PlateauResoniteLink.Application.Importing.ParsedCityObject terrainAlignedParsedCityObject =
            ParsedCityObjectTerrainPreparation.Prepare(parsedCityObject, terrainHeightSampler);
        List<ImportedCityObject> projectedCityObjects = [];
        List<ImportedCityObject> generatedRoadMarkings = [];

        foreach ((global::PlateauResoniteLink.Application.Importing.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject
                 in DemTerrainOverlayAssignment.SplitForValidatedTerrainProjection(
                     terrainAlignedParsedCityObject,
                     demTerrainTextureOverlays,
                     request.MeshCode,
                     requestedMeshAreas,
                     "project",
                     progressReporter,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportedCityObject cityObject = TerrainMeshModeProjector.Project(
                splitCityObject.CityObject,
                globalOriginPoint,
                globalCartesian,
                splitCityObject.Overlay,
                request,
                requestedMeshAreas,
                materialResolver,
                ProjectCityObject,
                progressReporter,
                cancellationToken);

            if (ImportedCityObjectGeometryPredicates.HasRenderableGeometry(cityObject))
            {
                projectedCityObjects.Add(cityObject);
            }

            ImportedCityObject? markingObject = GeneratedRoadMarkingProjection.TryProject(
                splitCityObject.CityObject,
                splitCityObject.Overlay,
                globalOriginPoint,
                globalCartesian,
                materialResolver,
                ProjectCityObject);
            if (markingObject is not null)
            {
                generatedRoadMarkings.Add(markingObject);
            }
        }

        foreach (ImportedCityObject cityObject in ProjectedCityObjectOutputOrdering.CreateOrderedOutput(
                     terrainAlignedParsedCityObject,
                     projectedCityObjects,
                     generatedRoadMarkings,
                     request.TerrainMeshMode,
                     cancellationToken))
        {
            yield return cityObject;
        }
    }

    internal static IEnumerable<MaterialBinding> EnumerateCommonMaterialsForParsedCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject parsedCityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver)
    {
        ArgumentNullException.ThrowIfNull(parsedCityObject);
        ArgumentNullException.ThrowIfNull(globalOriginPoint);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(materialResolver);

        global::PlateauResoniteLink.Application.Importing.ParsedCityObject terrainAlignedParsedCityObject =
            ParsedCityObjectTerrainPreparation.Prepare(parsedCityObject, terrainHeightSampler);

        foreach ((global::PlateauResoniteLink.Application.Importing.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject
                 in DemTerrainOverlayAssignment.SplitForValidatedTerrainProjection(
                     terrainAlignedParsedCityObject,
                     demTerrainTextureOverlays,
                     request.MeshCode,
                     requestedMeshAreas,
                     "common-material-enumeration"))
        {
            global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin = CityObjectGeometryMetrics.GetCenterOrigin(splitCityObject.CityObject);
            LocalCartesian? cityObjectCartesian = splitCityObject.CityObject.ReferenceSystem.IsGeographic
                ? new LocalCartesian(
                    cityObjectOrigin.Latitude,
                    cityObjectOrigin.Longitude,
                    cityObjectOrigin.Altitude,
                    splitCityObject.CityObject.ReferenceSystem.Geocentric)
                : null;

            foreach (MaterialBinding material in request.TerrainMeshMode is TerrainMeshMode.Grid or TerrainMeshMode.Dynamic
                         && string.Equals(splitCityObject.CityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
                            ? DemTerrainGridProjector.CreateMaterialPlan(
                                splitCityObject.CityObject,
                                cityObjectOrigin,
                                cityObjectCartesian,
                                splitCityObject.Overlay,
                                request.MeshCode,
                                requestedMeshAreas,
                                materialResolver).Materials
                            : CommonMaterialBindingEnumerator.CreateSharedBindings(
                                splitCityObject.CityObject,
                                cityObjectOrigin,
                                cityObjectCartesian,
                                splitCityObject.Overlay,
                                materialResolver))
            {
                yield return material;
            }
        }
    }

    private static Float3 ToContractFloat3(Float3 value) => new(value.X, value.Y, value.Z);

    internal static void ValidateCompatibleReferenceSystem(
        ProjectionCoordinateReferenceSystem expectedReferenceSystem,
        ProjectionCoordinateReferenceSystem? actualReferenceSystem)
    {
        if (actualReferenceSystem is null || expectedReferenceSystem.IsCompatibleWith(actualReferenceSystem))
        {
            return;
        }

        throw new PlateauImportValidationException(
            [$"Mixed CityGML coordinate reference systems are not supported. Found '{expectedReferenceSystem.SrsName}' and '{actualReferenceSystem.SrsName}'."]);
    }

}
