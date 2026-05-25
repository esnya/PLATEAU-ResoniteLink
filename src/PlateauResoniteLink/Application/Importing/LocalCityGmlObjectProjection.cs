using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

using Geocentric = GeographicLib.Geocentric;
using LocalCartesian = GeographicLib.LocalCartesian;

namespace PlateauResoniteLink.Application.Importing;

internal static class LocalCityGmlObjectProjection
{
    private const double UnknownRoofBottomAltitudeToleranceMeters = 0.1;
    public const string DefaultDemTerrainTexturePath = DemTerrainTextureDefaults.PlateauOrthoPath;
    public const string DefaultDemTerrainTextureUrlTemplate = DemTerrainTextureDefaults.PlateauOrthoUrlTemplate;
    public const string DefaultDemTerrainTextureFallbackUrlTemplate = DemTerrainTextureDefaults.GsiFallbackUrlTemplate;
    public const int DefaultDemTerrainTextureZoomLevel = DemTerrainTextureDefaults.PlateauOrthoZoomLevel;
    public const int DefaultDemTerrainTextureFallbackZoomLevel = DemTerrainTextureDefaults.FallbackZoomLevel;
    public const int DefaultDemTerrainTextureMaxSize = DemTerrainTextureDefaults.MaxTextureSize;
    public const double DefaultGeneratedRoadMarkingWidthMeters = GeneratedRoadMarkingCityObjectFactory.DefaultMarkingWidthMeters;
    public const double DefaultGeneratedRoadMarkingSegmentLengthMeters = GeneratedRoadMarkingCityObjectFactory.DefaultSegmentLengthMeters;
    public const double DefaultTerrainAlignedTransportationSegmentLengthMeters = TerrainAlignedTransportationSurfaceSplitter.DefaultSegmentLengthMeters;
    public const double MinTerrainAlignedTransportationSegmentLengthMeters = TerrainAlignedTransportationSurfaceSplitter.MinSegmentLengthMeters;
    public const double TerrainAlignedTransportationSegmentLengthByWidthRatio = TerrainAlignedTransportationSurfaceSplitter.SegmentLengthByWidthRatio;
    public static readonly MaterialDepthOffset DefaultTerrainAlignedMaterialDepthOffset = CityGmlSurfaceMaterialResolver.TerrainAlignedDepthOffset;

    private static readonly Quaternion GridMeshTerrainRotation = new(
        X: Math.Sqrt(0.5),
        Y: 0.0,
        Z: 0.0,
        W: Math.Sqrt(0.5));
    private static readonly ColorRgba DefaultMaterialColor = new(1.0, 1.0, 1.0, 1.0);
    private static readonly ColorRgba DefaultVegetationMaterialColor = new(0.32, 0.58, 0.24, 1.0);
    private static readonly XNamespace App = "http://www.opengis.net/citygml/appearance/2.0";
    private static readonly XNamespace Core = "http://www.opengis.net/citygml/2.0";
    private static readonly XNamespace Gml = "http://www.opengis.net/gml";
    internal static ParsedCityObject? ParseCityObject(
        XElement cityObjectElement,
        string packageName,
        string relativeSourceFile,
        string actualMeshCode,
        bool sharedAcrossMeshCodes,
        ICityGmlAppearanceStore appearanceStore,
        ICityGmlLodSelector lodSelector,
        CoordinateReferenceSystem coordinateReferenceSystem,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        LodFilteringStrategy lodFilteringStrategy)
    {
        string objectTypeName = cityObjectElement.Name.LocalName;
        string objectId = GetAttribute(cityObjectElement, Gml + "id") ?? objectTypeName;
        string? displayName = cityObjectElement.Elements(Gml + "name").FirstOrDefault()?.Value.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = objectId;
        }

        string resolvedActualMeshCode = CityGmlMeshCodeBoundsFilter.ResolveActualMeshCode(
            packageName,
            displayName!,
            objectId,
            actualMeshCode,
            sharedAcrossMeshCodes);
        BuildingAttributeContext buildingAttributes = BuildingAttributeParser.Parse(cityObjectElement);
        int? floorsAboveGround = BuildingAttributeQueries.TryGetKnownPositiveInteger(buildingAttributes.StoreysAboveGround);
        double? measuredHeightMeters = BuildingAttributeQueries.TryGetKnownPositiveMetric(buildingAttributes.MeasuredHeightMeters);

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

        ParsedSurface[] surfaces = preferredSurfaceElements
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

        if (!CityGmlMeshCodeBoundsFilter.IntersectsRequestedMeshCodeBounds(
                resolvedActualMeshCode,
                sharedAcrossMeshCodes,
                coordinateReferenceSystem,
                requestedMeshCodeBounds,
                surfaces))
        {
            return null;
        }

        string fileStem = Path.GetFileNameWithoutExtension(relativeSourceFile);
        string slotKey = SanitizeIdentifier($"{packageName}_{fileStem}_{objectId}");
        return new ParsedCityObject(
            slotKey,
            displayName!,
            packageName,
            resolvedActualMeshCode,
            lodLevel,
            surfaces,
            coordinateReferenceSystem,
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

    private static (XElement[] SurfaceElements, int? LodLevel) SelectPreferredLodSurfaceElements(
        XElement cityObjectElement,
        string packageName,
        bool isMarking,
        LodFilteringStrategy lodFilteringStrategy)
    {
        (XElement SurfaceElement, int? LodLevel)[] surfaces = cityObjectElement
            .Descendants()
            .Where(element => element.Name == Gml + "Polygon" || element.Name == Gml + "Triangle")
            .Select(surfaceElement => (surfaceElement, GetSurfaceLodLevel(surfaceElement, cityObjectElement)))
            .ToArray();

        int[] explicitLodLevels = surfaces
            .Where(static surface => surface.LodLevel.HasValue)
            .Select(static surface => surface.LodLevel!.Value)
            .ToArray();

        // Filter out excluded LODs before finding the highest
        int[] validLodLevels = explicitLodLevels
            .Where(lod => !lodFilteringStrategy.ShouldExcludeLod(packageName, lod, isMarking))
            .ToArray();

        int? highestLod = validLodLevels.Length > 0
            ? validLodLevels.Max()
            : null;

        XElement[] selectedSurfaces = highestLod.HasValue
            ? surfaces
                .Where(surface => surface.LodLevel == highestLod.Value)
                .Select(static surface => surface.SurfaceElement)
                .ToArray()
            : surfaces
                .Where(surface => !lodFilteringStrategy.ShouldExcludeLod(packageName, surface.LodLevel, isMarking))
                .Select(static surface => surface.SurfaceElement)
                .ToArray();

        return (selectedSurfaces, highestLod);
    }

    private static int? GetSurfaceLodLevel(XElement surfaceElement, XElement cityObjectElement)
    {
        for (XElement? ancestor = surfaceElement.Parent; ancestor is not null && ancestor != cityObjectElement; ancestor = ancestor.Parent)
        {
            if (TryParseLodLevel(ancestor.Name.LocalName, out int lodLevel))
            {
                return lodLevel;
            }
        }

        return null;
    }

    private static bool TryParseLodLevel(string localName, out int lodLevel)
    {
        lodLevel = 0;
        if (!localName.StartsWith("lod", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int digitStart = 3;
        int digitLength = 0;
        while (digitStart + digitLength < localName.Length
            && char.IsDigit(localName[digitStart + digitLength]))
        {
            digitLength++;
        }

        return digitLength > 0
            && int.TryParse(
                localName.AsSpan(digitStart, digitLength),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out lodLevel);
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
        IEnumerable<ParsedSourceFileResult> demParsedSourceFiles,
        MeshCodeBounds? fallbackBounds)
    {
        DemTerrainBounds? bounds = DemSourceDiscoverySupport.ResolveDemTerrainBounds(
            demParsedSourceFiles.Select(global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.FromProjectionModel),
            fallbackBounds is null ? null : DemTerrainBounds.FromProjectionModel(fallbackBounds));
        return bounds?.ToProjectionModel();
    }

    private static ProjectionTerrainHeightTriangle[] ExtractTerrainHeightTriangles(
        IEnumerable<ParsedCityObject> cityObjects)
    {
        return DemSourceDiscoverySupport.CreateTerrainHeightTriangles(
                cityObjects.Select(global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.FromProjectionModel))
            .Select(static triangle => triangle.ToProjectionModel())
            .ToArray();
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

        GeodeticPoint cityObjectOrigin = ResolveProjectionCityObjectOrigin(cityObject);
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

    private static List<global::PlateauResoniteLink.Application.Importing.ParsedSurface> SubdivideTransportationSurfaceForTerrainAlignment(
        global::PlateauResoniteLink.Application.Importing.ParsedSurface surface,
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject)
    {
        if (surface.InteriorRings.Length != 0
            || surface.ExteriorRing.Vertices.Length != 4)
        {
            return [surface];
        }

        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin = ResolveParsedCityObjectOrigin(cityObject);
        LocalCartesian? cityObjectCartesian = cityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObject.ReferenceSystem.Geocentric)
            : null;
        Float3[] positions = surface.ExteriorRing.Vertices
            .Select(point => CreateScenePosition(point.ToProjectionModel(), cityObjectOrigin.ToProjectionModel(), cityObjectCartesian))
            .ToArray();
        if (!IsNearHorizontalSurface(positions))
        {
            return [surface];
        }

        EdgePairSelection edgePair = RoadSurfaceEdgePairSelector.Select(
            global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.ToProjectionModel(surface.ExteriorRing),
            positions);
        ParsedSurface projectionSurface = global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.ToProjectionModel(surface);
        List<ParsedSurface> strips = TerrainAlignedTransportationSurfaceSplitter.Split(
            projectionSurface,
            positions,
            edgePair);
        if (strips.Count == 1 && ReferenceEquals(strips[0], projectionSurface))
        {
            return [surface];
        }

        return strips.Select(global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.FromProjectionModel).ToList();
    }

    private static bool IsTerrainDependentCityObject(ParsedCityObject cityObject)
    {
        return string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            || ShouldTerrainAlignCityObject(cityObject);
    }

    private static bool ShouldTerrainAlignCityObject(ParsedCityObject cityObject)
    {
        return CityGmlTerrainConformer.ShouldTerrainAlign(cityObject.PackageName, cityObject.LodLevel);
    }

    private static bool IsNearHorizontalSurface(Float3[] positions)
    {
        Float3? normal = ComputePolygonNormal(positions);
        return normal is not null && Math.Abs(normal.Y) >= 0.7;
    }

    private static GeodeticPoint ComputeGlobalOrigin(IEnumerable<ParsedCityObject> cityObjects)
    {
        return CreateGlobalOrigin(GetBounds(cityObjects), requestedMeshCodeBounds: null, isGeographicReferenceSystem: false);
    }

    private static GeodeticPoint CreateGlobalOrigin(
        (double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double minAltitude) bounds,
        MeshCodeBounds? requestedMeshCodeBounds,
        bool isGeographicReferenceSystem)
    {
        if (isGeographicReferenceSystem && requestedMeshCodeBounds is not null)
        {
            return new GeodeticPoint(
                Latitude: (requestedMeshCodeBounds.SouthLatitude + requestedMeshCodeBounds.NorthLatitude) / 2.0,
                Longitude: (requestedMeshCodeBounds.WestLongitude + requestedMeshCodeBounds.EastLongitude) / 2.0,
                Altitude: bounds.minAltitude);
        }

        return new GeodeticPoint(
            Latitude: (bounds.minLatitude + bounds.maxLatitude) / 2.0,
            Longitude: (bounds.minLongitude + bounds.maxLongitude) / 2.0,
            Altitude: bounds.minAltitude);
    }

    private static CoordinateReferenceSystem GetReferenceSystem(IEnumerable<ParsedCityObject> cityObjects)
    {
        CoordinateReferenceSystem? referenceSystem = null;

        foreach (ParsedCityObject cityObject in cityObjects)
        {
            if (referenceSystem is null)
            {
                referenceSystem = cityObject.ReferenceSystem;
                continue;
            }

            if (!referenceSystem.IsCompatibleWith(cityObject.ReferenceSystem))
            {
                throw new PlateauImportValidationException(
                    [$"Mixed CityGML coordinate reference systems are not supported. Found '{referenceSystem.SrsName}' and '{cityObject.ReferenceSystem.SrsName}'."]);
            }
        }

        return referenceSystem
            ?? throw new PlateauImportValidationException(["No CityGML coordinate reference system was resolved."]);
    }

    internal static ImportedCityObject ProjectCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        double? geometryHeightMeters = cityObject.GeometryHeightMeters
            ?? ResolveParsedGeometryHeightMeters(cityObject.Surfaces);
        cityObject = GeneratedLod1RoofCityObjectFactory.Create(cityObject) with
        {
            GeometryHeightMeters = geometryHeightMeters,
        };
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin = ResolveParsedCityObjectOrigin(cityObject);

        LocalCartesian? cityObjectCartesian = cityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObject.ReferenceSystem.Geocentric)
            : null;
        Float3 slotPosition = CreateScenePosition(
            cityObjectOrigin.ToProjectionModel(),
            globalOriginPoint.ToProjectionModel(),
            globalCartesian);
        List<MeshVertex> vertices = [];
        List<MeshSubmesh> submeshes = [];
        List<MaterialBinding> materials = [];
        DemUvProjection? demUvProjection = TryCreateDemUvProjection(cityObject.ActualMeshCode, demTerrainTextureOverlay);

        List<ResolvedSurfaceMaterial> resolvedSurfaces =
        [
            .. CityGmlSurfaceMaterialResolver.ResolveSurfaces(
                cityObject,
                cityObjectOrigin,
                cityObjectCartesian,
                demTerrainTextureOverlay,
                materialResolver),
        ];

        IGrouping<MaterialGroupingKey, ResolvedSurfaceMaterial>[] materialGroups = resolvedSurfaces
            .GroupBy(
                resolvedSurface => MaterialGroupingPolicy.CreateKey(
                    cityObject.ActualMeshCode,
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor,
                    resolvedSurface.Material.TextureOffset))
            .OrderBy(static group => group.Min(static surface => ParsedSurfaceStableSortKey.Create(surface.Surface)), StringComparer.Ordinal)
            .ToArray();
        FacadeUvProjectionContext? facadeUvProjectionContext = CityGmlSurfaceProjectionPolicy.TryCreateFacadeUvProjectionContext(
            cityObject.PackageName,
            cityObject.Surfaces,
            cityObjectOrigin,
            cityObjectCartesian);

        for (int materialIndex = 0; materialIndex < materialGroups.Length; materialIndex++)
        {
            IGrouping<MaterialGroupingKey, ResolvedSurfaceMaterial> materialGroup = materialGroups[materialIndex];
            List<int> indices = [];

            foreach (ResolvedSurfaceMaterial resolvedSurface in materialGroup
                         .OrderBy(static surface => ParsedSurfaceStableSortKey.Create(surface.Surface), StringComparer.Ordinal))
            {
                SurfaceMeshTessellation tessellation = CityGmlSurfaceMeshTessellator.Tessellate(
                    new SurfaceMeshTessellationRequest(
                        cityObject.PackageName,
                        resolvedSurface.Surface,
                        resolvedSurface.Material,
                        cityObjectOrigin.ToProjectionModel(),
                        cityObjectCartesian,
                        facadeUvProjectionContext,
                        demUvProjection));
                int baseIndex = vertices.Count;
                vertices.AddRange(tessellation.Vertices);
                foreach (int index in tessellation.Indices)
                {
                    indices.Add(baseIndex + index);
                }
            }

            if (indices.Count == 0)
            {
                continue;
            }

            ResolvedSurfaceMaterial representativeSurface = materialGroup.First();
            submeshes.Add(new MeshSubmesh(materialIndex, indices));
            materials.Add(CityGmlSurfaceMaterialResolver.CreateMaterialBinding(cityObject.ActualMeshCode, representativeSurface, materialIndex));
        }

        return new ImportedCityObject(
            ObjectKey: cityObject.SlotKey,
            DisplayName: cityObject.DisplayName,
            PackageName: cityObject.PackageName,
            ActualMeshCode: cityObject.ActualMeshCode,
            LodLevel: cityObject.LodLevel,
            Transform: new Transform3D(ToContractFloat3(slotPosition)),
            Mesh: new ImportedMesh(vertices.ToArray(), submeshes.ToArray()),
            Materials: materials,
            SourceFileRelativePath: cityObject.SourceFileRelativePath);
    }

    private static GeodeticPoint ResolveProjectionCityObjectOrigin(ParsedCityObject cityObject)
    {
        return CityObjectOriginResolver.Resolve(
            cityObject.GeodeticOriginOverride,
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices),
            static point => point.Latitude,
            static point => point.Longitude,
            static point => point.Altitude,
            static (latitude, longitude, altitude) => new GeodeticPoint(latitude, longitude, altitude));
    }

    private static global::PlateauResoniteLink.Application.Importing.GeodeticPoint ResolveParsedCityObjectOrigin(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject)
    {
        return CityObjectOriginResolver.Resolve(
            cityObject.GeodeticOriginOverride,
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
    }

    private static double ResolveProjectionMinimumAltitude(IEnumerable<ParsedSurface> surfaces)
    {
        return CityObjectAltitudeMetricsResolver.GetMinimumAltitude(
            surfaces.SelectMany(static surface => surface.Vertices),
            static point => point.Altitude);
    }

    private static double ResolveParsedMinimumAltitude(
        IEnumerable<global::PlateauResoniteLink.Application.Importing.ParsedSurface> surfaces)
    {
        return CityObjectAltitudeMetricsResolver.GetMinimumAltitude(
            surfaces.SelectMany(static surface => surface.Vertices));
    }

    private static double? ResolveParsedGeometryHeightMeters(
        IEnumerable<global::PlateauResoniteLink.Application.Importing.ParsedSurface> surfaces)
    {
        return CityObjectAltitudeMetricsResolver.TryGetGeometryHeightMeters(
            surfaces.SelectMany(static surface => surface.Vertices));
    }

    private static ResolvedSurfaceMaterial ResolveSurfaceMaterial(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        ParsedSurface surface,
        double cityObjectMinAltitude,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        if (surface.UsesGeneratedDemTexture)
        {
            return new ResolvedSurfaceMaterial(
                surface,
                new ResolvedMaterial(
                    MaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind.Dataset,
                    MaterialProjection.Uv,
                    Family: null,
                    TextureScale: null,
                    ReuseScope: MaterialReuseScope.PerObject,
                    TerrainOverlay: demTerrainTextureOverlay),
                DepthOffset: null);
        }

        ResolvedMaterial? roofTerrainTextureMaterial = TryCreateRoofTerrainTextureMaterial(
            cityObject.ActualMeshCode,
            cityObject.PackageName,
            surface,
            cityObjectMinAltitude,
            demTerrainTextureOverlay,
            cityObjectOrigin,
            cityObjectCartesian);
        if (roofTerrainTextureMaterial is not null)
        {
            return new ResolvedSurfaceMaterial(
                surface with { BaseColor = DefaultMaterialColor },
                roofTerrainTextureMaterial,
                DepthOffset: null);
        }

        if (string.Equals(cityObject.PackageName, "veg", StringComparison.OrdinalIgnoreCase)
            && surface.TexturePayload is null)
        {
            if (HasExplicitMaterialColor(surface.BaseColor))
            {
                return new ResolvedSurfaceMaterial(
                    surface,
                    new ResolvedMaterial(
                        MaterialType.VertexColor,
                        TexturePayload: null,
                        TextureSourceKind.Bundled,
                        MaterialProjection.Uv,
                        Family: null,
                        TextureScale: null,
                        ReuseScope: MaterialReuseScope.PerObject),
                    DepthOffset: null);
            }

            return new ResolvedSurfaceMaterial(
                surface with { BaseColor = DefaultVegetationMaterialColor },
                new ResolvedMaterial(
                    MaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind.Bundled,
                    MaterialProjection.Uv,
                    Family: null,
                    TextureScale: null,
                    ReuseScope: MaterialReuseScope.PerObject),
                DepthOffset: null);
        }

        if (IsGeneratedRoadMarkingSurface(surface))
        {
            return new ResolvedSurfaceMaterial(
                surface,
                new ResolvedMaterial(
                    MaterialType.VertexColor,
                    TexturePayload: null,
                    TextureSourceKind.Bundled,
                    MaterialProjection.Uv,
                    Family: null,
                    TextureScale: null,
                    ReuseScope: MaterialReuseScope.PerObject),
                DefaultTerrainAlignedMaterialDepthOffset);
        }

        bool preferUvProjection = ShouldPreferUvProjection(
            cityObject.PackageName,
            surface,
            cityObjectOrigin,
            cityObjectCartesian);
        ResolvedMaterial resolvedMaterial = materialResolver.ResolveMaterial(new DefaultMaterialRequest(
            cityObject.PackageName,
            surface.TexturePayload,
            preferUvProjection,
            FamilyOverride: null,
            VariantSelectionKey: $"{cityObject.SlotKey}:{(preferUvProjection ? "uv" : "triplanar")}",
            BuildingAttributes: cityObject.BuildingAttributes,
            FloorsAboveGround: cityObject.FloorsAboveGround,
            MeasuredHeightMeters: cityObject.MeasuredHeightMeters,
            GeometryHeightMeters: cityObject.GeometryHeightMeters,
            FootprintAreaSquareMeters: cityObject.BuildingAttributes is null
                ? null
                : BuildingAttributeQueries.TryGetKnownPositiveMetric(cityObject.BuildingAttributes.BuildingFootprintArea),
            SurfaceRole: ToDefaultMaterialSurfaceRole(surface.Semantic)));
        MaterialDepthOffset? depthOffset = cityObject.TerrainAligned
            ? DefaultTerrainAlignedMaterialDepthOffset
            : null;
        return new ResolvedSurfaceMaterial(surface, resolvedMaterial, depthOffset);
    }

    private static ResolvedMaterial? TryCreateRoofTerrainTextureMaterial(
        string actualMeshCode,
        string packageName,
        ParsedSurface surface,
        double cityObjectMinAltitude,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (demTerrainTextureOverlay is null
            || TerrainOverlayMeshCodeResolver.ResolveMeshCode(actualMeshCode, demTerrainTextureOverlay) is null
            || surface.TexturePayload is not null
            || !IsBuildingPackage(packageName)
            || !IsRoofTerrainTextureSurface(surface, cityObjectMinAltitude, cityObjectOrigin, cityObjectCartesian))
        {
            return null;
        }

        return new ResolvedMaterial(
            MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind.Dataset,
            MaterialProjection.Uv,
            Family: null,
            TextureScale: null,
            ReuseScope: MaterialReuseScope.PerObject,
            TerrainOverlay: demTerrainTextureOverlay);
    }

    private static bool IsRoofTerrainTextureSurface(
        ParsedSurface surface,
        double cityObjectMinAltitude,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (surface.Semantic == ParsedSurfaceSemantic.Roof)
        {
            return true;
        }

        if (surface.Semantic is not (ParsedSurfaceSemantic.Unknown
            or ParsedSurfaceSemantic.Ground
            or ParsedSurfaceSemantic.OuterCeiling
            or ParsedSurfaceSemantic.OuterFloor))
        {
            return false;
        }

        Float3? normal = ComputeSurfaceNormal(surface, cityObjectOrigin, cityObjectCartesian);
        return normal is not null
            && Math.Abs(normal.Y) >= 0.98
            && IsAboveCityObjectBottomAltitude(surface, cityObjectMinAltitude);
    }

    private static bool IsAboveCityObjectBottomAltitude(
        ParsedSurface surface,
        double cityObjectMinAltitude)
    {
        double surfaceMinAltitude = surface.Vertices.Min(static vertex => vertex.Altitude);
        return surfaceMinAltitude > cityObjectMinAltitude + UnknownRoofBottomAltitudeToleranceMeters;
    }

    private static DemUvProjection? TryCreateDemUvProjection(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        if (demTerrainTextureOverlay is null
            || TerrainOverlayMeshCodeResolver.ResolveMeshCode(cityObject.ActualMeshCode, demTerrainTextureOverlay) is not { } terrainMeshCode
            || !TryCreateMeshCodeBounds(terrainMeshCode, out MeshCodeBounds? meshCodeBounds))
        {
            return null;
        }

        return CreateDemUvProjection(
            meshCodeBounds!.WestLongitude,
            meshCodeBounds.EastLongitude,
            meshCodeBounds.NorthLatitude,
            meshCodeBounds.SouthLatitude);
    }

    private static DemUvProjection? TryCreateDemUvProjection(
        ParsedCityObject cityObject,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        return TryCreateDemUvProjection(cityObject.ActualMeshCode, demTerrainTextureOverlay);
    }

    private static DemUvProjection? TryCreateDemUvProjection(
        string actualMeshCode,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        if (demTerrainTextureOverlay is null
            || TerrainOverlayMeshCodeResolver.ResolveMeshCode(actualMeshCode, demTerrainTextureOverlay) is not { } terrainMeshCode
            || !TryCreateMeshCodeBounds(terrainMeshCode, out MeshCodeBounds? meshCodeBounds))
        {
            return null;
        }

        return CreateDemUvProjection(
            meshCodeBounds!.WestLongitude,
            meshCodeBounds.EastLongitude,
            meshCodeBounds.NorthLatitude,
            meshCodeBounds.SouthLatitude);
    }

    private static DemUvProjection CreateDemUvProjection(
        double westLongitude,
        double eastLongitude,
        double northLatitude,
        double southLatitude)
    {
        double west = WebMercatorTileMath.LongitudeToNormalizedX(westLongitude);
        double east = WebMercatorTileMath.LongitudeToNormalizedX(eastLongitude);
        double north = WebMercatorTileMath.LatitudeToNormalizedY(northLatitude);
        double south = WebMercatorTileMath.LatitudeToNormalizedY(southLatitude);
        double width = Math.Max(east - west, 1e-12);
        double height = Math.Max(south - north, 1e-12);

        return new DemUvProjection(west, south, width, height);
    }

    private static bool IsBuildingPackage(string packageName)
    {
        return string.Equals(packageName, "bldg", StringComparison.Ordinal)
            || string.Equals(packageName, "ubld", StringComparison.Ordinal);
    }

    private static Float3? ComputePolygonNormal(IEnumerable<Float3> positions)
    {
        Float3[] points = positions.ToArray();
        if (points.Length < 3)
        {
            return null;
        }

        double normalX = 0.0;
        double normalY = 0.0;
        double normalZ = 0.0;

        for (int index = 0; index < points.Length; index++)
        {
            Float3 current = points[index];
            Float3 next = points[(index + 1) % points.Length];
            normalX += (current.Y - next.Y) * (current.Z + next.Z);
            normalY += (current.Z - next.Z) * (current.X + next.X);
            normalZ += (current.X - next.X) * (current.Y + next.Y);
        }

        double magnitude = Math.Sqrt((normalX * normalX) + (normalY * normalY) + (normalZ * normalZ));
        if (magnitude < 1e-8)
        {
            return null;
        }

        return new Float3(normalX / magnitude, normalY / magnitude, normalZ / magnitude);
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

    private static string CreateTerrainOverlayToken(TerrainTextureOverlay terrainOverlay)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"terrain-overlay-{terrainOverlay.PackageName.ToLowerInvariant()}-{terrainOverlay.SourceDescriptorKey}-bounds-{FormatBounds(terrainOverlay.GeographicBounds)}");
    }

    private static string MaterialTypeToken(MaterialType materialType) =>
        materialType.ToString().ToLowerInvariant();

    private static string ProjectionToken(MaterialProjection projection)
    {
        return projection switch
        {
            MaterialProjection.Uv => "uv",
            MaterialProjection.Triplanar => "triplanar",
            _ => projection.ToString().ToLowerInvariant(),
        };
    }

    private static string FormatFloat2(Float2? value)
    {
        return value is null
            ? "none"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{FormatRounded(value.X)}-{FormatRounded(value.Y)}");
    }

    private static string FormatDepth(MaterialDepthOffset? value)
    {
        return value is null
            ? "none"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{FormatRounded(value.Factor)}-{FormatRounded(value.Units)}");
    }

    private static string FormatColor(ColorRgba value) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatRounded(value.R)}-{FormatRounded(value.G)}-{FormatRounded(value.B)}-{FormatRounded(value.A)}");

    private static string FormatBounds(GeographicRectangle bounds) =>
        TerrainOverlayDiagnostics.FormatBounds(bounds);

    private static string FormatRounded(double value) =>
        TerrainOverlayDiagnostics.FormatRounded(value);

    private static bool ShouldPreferUvProjection(
        string packageName,
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (surface.TexturePayload is not null)
        {
            return true;
        }

        if (string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsBuildingPackage(packageName))
        {
            return PlateauPackageCatalog.IsPathLikePackage(packageName)
                && IsNearHorizontalSurface(surface, cityObjectOrigin, cityObjectCartesian);
        }

        if (surface.Semantic is ParsedSurfaceSemantic.Wall)
        {
            return true;
        }

        if (surface.Semantic is ParsedSurfaceSemantic.Roof
            or ParsedSurfaceSemantic.Ground
            or ParsedSurfaceSemantic.OuterCeiling
            or ParsedSurfaceSemantic.OuterFloor)
        {
            return false;
        }

        return IsFacadeSurface(surface, cityObjectOrigin, cityObjectCartesian);
    }

    private static DefaultMaterialSurfaceRole ToDefaultMaterialSurfaceRole(ParsedSurfaceSemantic semantic)
    {
        return semantic switch
        {
            ParsedSurfaceSemantic.Wall => DefaultMaterialSurfaceRole.Wall,
            ParsedSurfaceSemantic.Roof => DefaultMaterialSurfaceRole.Roof,
            ParsedSurfaceSemantic.Ground => DefaultMaterialSurfaceRole.Ground,
            ParsedSurfaceSemantic.Closure => DefaultMaterialSurfaceRole.Closure,
            ParsedSurfaceSemantic.OuterCeiling => DefaultMaterialSurfaceRole.OuterCeiling,
            ParsedSurfaceSemantic.OuterFloor => DefaultMaterialSurfaceRole.OuterFloor,
            _ => DefaultMaterialSurfaceRole.Unknown,
        };
    }

    private static bool IsFacadeSurface(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        return CityGmlSurfaceProjectionPolicy.IsFacadeSurface(
            surface,
            cityObjectOrigin,
            cityObjectCartesian);
    }

    private static bool IsNearHorizontalSurface(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        return CityGmlSurfaceProjectionPolicy.IsNearHorizontalSurface(
            surface,
            cityObjectOrigin,
            cityObjectCartesian);
    }

    private static HashSet<string> GetCulledSurfaceIdsBeforeProjection(
        string packageName,
        IEnumerable<ParsedSurface> surfaces,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        return CityGmlSurfaceProjectionPolicy.GetCulledSurfaceIdsBeforeProjection(
            packageName,
            surfaces,
            cityObjectOrigin,
            cityObjectCartesian);
    }

    private static Float3? ComputeSurfaceNormal(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        return CityGmlSurfaceProjectionPolicy.ComputeSurfaceNormal(
            surface,
            cityObjectOrigin,
            cityObjectCartesian);
    }

    private static FacadeUvProjectionContext? TryCreateFacadeUvProjectionContext(
        string packageName,
        IEnumerable<ParsedSurface> surfaces,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        return CityGmlSurfaceProjectionPolicy.TryCreateFacadeUvProjectionContext(
            packageName,
            surfaces,
            cityObjectOrigin,
            cityObjectCartesian);
    }

    private static bool IsGeneratedRoadMarkingSurface(ParsedSurface surface)
    {
        return surface.PolygonId.Contains("_generated_marking", StringComparison.Ordinal);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string? GetAttribute(XElement element, XName attributeName)
    {
        return element.Attribute(attributeName)?.Value;
    }

    private static bool AreSamePoint(GeodeticPoint left, GeodeticPoint right)
    {
        return Math.Abs(left.Latitude - right.Latitude) < 1e-8
            && Math.Abs(left.Longitude - right.Longitude) < 1e-8
            && Math.Abs(left.Altitude - right.Altitude) < 1e-8;
    }

    private static bool HasExplicitMaterialColor(ColorRgba color)
    {
        return Math.Abs(color.R - DefaultMaterialColor.R) >= 1e-8
            || Math.Abs(color.G - DefaultMaterialColor.G) >= 1e-8
            || Math.Abs(color.B - DefaultMaterialColor.B) >= 1e-8
            || Math.Abs(color.A - DefaultMaterialColor.A) >= 1e-8;
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
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
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

        CoordinateReferenceSystem projectionReferenceSystem = referenceSystem.ToProjectionModel();
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

    internal static IEnumerable<ImportedCityObject> ProjectParsedCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject parsedCityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
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

        double? geometryHeightMeters = ResolveParsedGeometryHeightMeters(parsedCityObject.Surfaces);
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject terrainAlignedParsedCityObject =
            GeneratedLod1RoofCityObjectFactory.Create(ConformCityObjectToTerrain(parsedCityObject, terrainHeightSampler)) with
            {
                GeometryHeightMeters = geometryHeightMeters,
            };
        List<ImportedCityObject> projectedCityObjects = [];
        List<ImportedCityObject> generatedRoadMarkings = [];

        foreach ((global::PlateauResoniteLink.Application.Importing.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject
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

            global::PlateauResoniteLink.Application.Importing.GeodeticPoint markingOrigin = ResolveParsedCityObjectOrigin(splitCityObject.CityObject);
            LocalCartesian? markingCartesian = splitCityObject.CityObject.ReferenceSystem.IsGeographic
                ? new LocalCartesian(
                    markingOrigin.Latitude,
                    markingOrigin.Longitude,
                    markingOrigin.Altitude,
                    splitCityObject.CityObject.ReferenceSystem.Geocentric)
                : null;
            global::PlateauResoniteLink.Application.Importing.ParsedCityObject? roadMarkingCityObject = GeneratedRoadMarkingCityObjectFactory.Create(
                splitCityObject.CityObject,
                markingOrigin,
                markingCartesian);
            if (roadMarkingCityObject is null)
            {
                continue;
            }

            ImportedCityObject markingObject = ProjectCityObject(
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

    internal static IEnumerable<MaterialBinding> EnumerateCommonMaterialsForParsedCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject parsedCityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
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

        double? geometryHeightMeters = ResolveParsedGeometryHeightMeters(parsedCityObject.Surfaces);
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject terrainAlignedParsedCityObject =
            GeneratedLod1RoofCityObjectFactory.Create(ConformCityObjectToTerrain(parsedCityObject, terrainHeightSampler)) with
            {
                GeometryHeightMeters = geometryHeightMeters,
            };

        foreach ((global::PlateauResoniteLink.Application.Importing.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay) splitCityObject
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

            global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin = ResolveParsedCityObjectOrigin(splitCityObject.CityObject);
            LocalCartesian? cityObjectCartesian = splitCityObject.CityObject.ReferenceSystem.IsGeographic
                ? new LocalCartesian(
                    cityObjectOrigin.Latitude,
                    cityObjectOrigin.Longitude,
                    cityObjectOrigin.Altitude,
                    splitCityObject.CityObject.ReferenceSystem.Geocentric)
                : null;

            foreach (MaterialBinding material in request.TerrainMeshMode is TerrainMeshMode.Grid or TerrainMeshMode.Dynamic
                         && string.Equals(splitCityObject.CityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
                            ? CityGmlSurfaceMaterialResolver.CreateDemTerrainGridMaterials(
                                splitCityObject.CityObject,
                                cityObjectOrigin,
                                cityObjectCartesian,
                                splitCityObject.Overlay,
                                request.MeshCode,
                                requestedMeshCodeBounds,
                                materialResolver)
                            : CityGmlSurfaceMaterialResolver.CreateSharedCommonMaterialBindings(
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

    private static ImportedCityObject ProjectTerrainMeshModeCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
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
            return ProjectCityObject(cityObject, globalOriginPoint, globalCartesian, demTerrainTextureOverlay, materialResolver);
        }

        bool hasGrid = TryProjectDemTerrainGridCityObject(
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
                : ProjectCityObject(cityObject, globalOriginPoint, globalCartesian, demTerrainTextureOverlay, materialResolver);
        }

        if (request.TerrainMeshMode == TerrainMeshMode.Grid)
        {
            return heightMapCityObject!;
        }

        ImportedCityObject staticCityObject = ProjectCityObject(
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

    private static ImportedCityObject CreateNonRenderableCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject)
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

    private static global::PlateauResoniteLink.Application.Importing.ParsedCityObject ConformCityObjectToTerrain(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject parsedCityObject,
        ProjectionTerrainHeightSampler? terrainHeightSampler)
    {
        if (terrainHeightSampler is null
            || !ShouldTerrainAlignCityObject(parsedCityObject))
        {
            return parsedCityObject;
        }

        global::PlateauResoniteLink.Application.Importing.ParsedCityObject subdividedCityObject =
            SubdivideTerrainAlignedCityObject(parsedCityObject);
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint cityObjectOrigin = ResolveParsedCityObjectOrigin(subdividedCityObject);
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

    private static global::PlateauResoniteLink.Application.Importing.ParsedCityObject SubdivideTerrainAlignedCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject)
    {
        if (!ShouldSubdivideTerrainAlignedCityObject(cityObject))
        {
            return cityObject;
        }

        List<global::PlateauResoniteLink.Application.Importing.ParsedSurface> subdividedSurfaces = [];
        foreach (global::PlateauResoniteLink.Application.Importing.ParsedSurface surface in cityObject.Surfaces)
        {
            subdividedSurfaces.AddRange(SubdivideTransportationSurfaceForTerrainAlignment(surface, cityObject));
        }

        return subdividedSurfaces.Count == cityObject.Surfaces.Length
            ? cityObject
            : cityObject with { Surfaces = subdividedSurfaces.ToArray() };
    }

    private static bool ShouldSubdivideTerrainAlignedCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject)
    {
        return ShouldSubdivideTerrainAlignedCityObject(cityObject.PackageName, cityObject.LodLevel);
    }

    private static bool ShouldTerrainAlignCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject)
    {
        return CityGmlTerrainConformer.ShouldTerrainAlign(cityObject.PackageName, cityObject.LodLevel);
    }

    private static bool ShouldSubdivideTerrainAlignedCityObject(string packageName, int? lodLevel)
    {
        return PlateauPackageCatalog.IsRoadPackage(packageName)
            && (!lodLevel.HasValue || lodLevel.Value < 3);
    }

    private static bool ShouldTerrainAlignCityObject(string packageName, int? lodLevel)
    {
        return CityGmlTerrainConformer.ShouldTerrainAlign(packageName, lodLevel);
    }

    private static MaterialBinding[] CreateCommonMaterialBindings(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        HashSet<string> culledSurfaceIds = GetCulledSurfaceIdsBeforeProjection(
            cityObject.PackageName,
            cityObject.Surfaces,
            cityObjectOrigin,
            cityObjectCartesian);
        double cityObjectMinAltitude = ResolveProjectionMinimumAltitude(cityObject.Surfaces);
        List<ResolvedSurfaceMaterial> resolvedSurfaces =
        [
            .. cityObject.Surfaces
                .Where(surface => !culledSurfaceIds.Contains(surface.PolygonId))
                .Select(surface => ResolveSurfaceMaterial(
                    cityObject,
                    cityObjectOrigin,
                    cityObjectCartesian,
                    surface,
                    cityObjectMinAltitude,
                    demTerrainTextureOverlay,
                    materialResolver)),
        ];

        return resolvedSurfaces
            .GroupBy(
                resolvedSurface => MaterialGroupingPolicy.CreateKey(
                    cityObject.ActualMeshCode,
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor,
                    resolvedSurface.Material.TextureOffset))
            .OrderBy(static group => group.Min(static surface => ParsedSurfaceStableSortKey.Create(surface.Surface)), StringComparer.Ordinal)
            .Select((group, materialIndex) =>
            {
                ResolvedSurfaceMaterial representativeSurface = group.First();
                return CreateMaterialBinding(
                    cityObject.ActualMeshCode,
                    representativeSurface,
                    materialIndex);
            })
            .Where(static material => material.ReuseScope == MaterialReuseScope.Shared)
            .ToArray();
    }

    private static bool TryProjectDemTerrainGridCityObject(
        ParsedCityObject cityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        PlateauImportRequest request,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        IDefaultMaterialResolver materialResolver,
        out ImportedCityObject? heightMapCityObject)
    {
        heightMapCityObject = null;

        GeodeticPoint cityObjectOrigin = ResolveProjectionCityObjectOrigin(cityObject);
        LocalCartesian? cityObjectCartesian = cityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObject.ReferenceSystem.Geocentric)
            : null;
        if (cityObjectCartesian is null)
        {
            return false;
        }

        Float3 slotPosition = CreateScenePosition(cityObjectOrigin, globalOriginPoint, globalCartesian);
        Float3[] positions = cityObject.Surfaces
            .SelectMany(static surface => surface.Vertices)
            .Select(point => CreateGlobalTerrainGridLocalPosition(point, slotPosition, globalOriginPoint, globalCartesian))
            .ToArray();
        TerrainGridTriangle[] triangles = CreateDemTerrainGridTriangles(cityObject, slotPosition, globalOriginPoint, globalCartesian);
        double seaLevelLocalHeight = CreateGlobalTerrainGridLocalPosition(
            new GeodeticPoint(cityObjectOrigin.Latitude, cityObjectOrigin.Longitude, 0.0),
            slotPosition,
            globalOriginPoint,
            globalCartesian).Y;
        if (positions.Length < 3)
        {
            return false;
        }

        DemTerrainGridBounds heightMapBounds = CreateDemTerrainGridBounds(
            cityObject,
            cityObjectOrigin,
            slotPosition,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            positions);
        double minX = heightMapBounds.MinX;
        double maxX = heightMapBounds.MaxX;
        double minZ = heightMapBounds.MinZ;
        double maxZ = heightMapBounds.MaxZ;
        double centerX = (minX + maxX) / 2.0;
        double centerZ = (minZ + maxZ) / 2.0;
        double extentX = maxX - minX;
        double extentZ = maxZ - minZ;
        if (extentX <= 1e-6 || extentZ <= 1e-6)
        {
            return false;
        }

        DemTerrainGridHeightSamples heightSamples = CityGmlDemTerrainGridSampler.Sample(
            minX,
            maxX,
            minZ,
            maxZ,
            request.TerrainGridMetersPerVertex,
            request.TerrainGridMaxResolution,
            seaLevelLocalHeight,
            triangles);
        int width = heightSamples.Width;
        int height = heightSamples.Height;
        double[] localHeights = heightSamples.LocalHeights;
        double minHeight = localHeights.Min();
        double maxHeight = localHeights.Max();

        MaterialBinding[] materials = CreateDemTerrainGridMaterials(
            cityObject,
            cityObjectOrigin,
            cityObjectCartesian,
            demTerrainTextureOverlay,
            request.MeshCode,
            requestedMeshCodeBounds,
            materialResolver);
        if (materials.Length == 0)
        {
            return false;
        }

        TextureUvRect? heightMapOccupiedUvRect = TryCreateDemTerrainGridOccupiedUvRect(
            cityObject,
            cityObjectOrigin,
            cityObjectCartesian,
            demTerrainTextureOverlay,
            materialResolver);
        Float2? heightMapUvScale = heightMapOccupiedUvRect.HasValue
            ? ToContractFloat2(heightMapOccupiedUvRect.Value.ScaleValue)
            : null;
        Float2? heightMapUvOffset = heightMapOccupiedUvRect.HasValue
            ? ToContractFloat2(heightMapOccupiedUvRect.Value.OffsetValue)
            : null;

        Float3 adjustedSlotPosition = slotPosition with
        {
            // Elements.Assets.Grid centers vertices in-plane, so split DEM chunks need their own bbox-center offset here.
            X = slotPosition.X + centerX,
            // GridMesh displaces the inverted terrain grid downward in world Y, so the slot must start at the patch-local maximum height.
            Y = slotPosition.Y + maxHeight,
            Z = slotPosition.Z + centerZ,
        };

        heightMapCityObject = new ImportedCityObject(
            ObjectKey: cityObject.SlotKey,
            DisplayName: cityObject.DisplayName,
            PackageName: cityObject.PackageName,
            ActualMeshCode: cityObject.ActualMeshCode,
            LodLevel: cityObject.LodLevel,
            Transform: new Transform3D(
                ToContractFloat3(adjustedSlotPosition),
                ToContractQuaternion(GridMeshTerrainRotation)),
            Geometry: new TerrainGridGeometry(
                Width: width,
                Height: height,
                Size: new Float2(extentX, extentZ),
                MinHeight: minHeight,
                MaxHeight: maxHeight,
                HeightSamples: localHeights,
                UvScale: heightMapUvScale,
                UvOffset: heightMapUvOffset),
            Materials: materials,
            SourceFileRelativePath: cityObject.SourceFileRelativePath);
        return true;
    }

    private static bool TryProjectDemTerrainGridCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        PlateauImportRequest request,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        IDefaultMaterialResolver materialResolver,
        Action<string>? progressReporter,
        CancellationToken cancellationToken,
        out ImportedCityObject? heightMapCityObject)
    {
        return CityGmlDemTerrainGridCityObjectProjection.TryProject(
            cityObject,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            request,
            requestedMeshCodeBounds,
            materialResolver,
            progressReporter,
            cancellationToken,
            out heightMapCityObject);
    }

    private static MaterialBinding[] CreateDemTerrainGridMaterials(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        IDefaultMaterialResolver materialResolver)
    {
        HashSet<string> culledSurfaceIds = GetCulledSurfaceIdsBeforeProjection(
            cityObject.PackageName,
            cityObject.Surfaces,
            cityObjectOrigin,
            cityObjectCartesian);
        double cityObjectMinAltitude = ResolveProjectionMinimumAltitude(cityObject.Surfaces);
        List<ResolvedSurfaceMaterial> resolvedSurfaces =
        [
            .. cityObject.Surfaces
                .Where(surface => !culledSurfaceIds.Contains(surface.PolygonId))
                .Select(surface => ResolveSurfaceMaterial(
                    cityObject,
                    cityObjectOrigin,
                    cityObjectCartesian,
                    surface,
                    cityObjectMinAltitude,
                    demTerrainTextureOverlay,
                    materialResolver)),
        ];

        return resolvedSurfaces
            .GroupBy(
                resolvedSurface => MaterialGroupingPolicy.CreateKey(
                    TerrainOverlayMeshCodeResolver.ResolveMaterialMeshCodeSource(
                        cityObject.ActualMeshCode,
                        requestedMeshCode,
                        requestedMeshCodeBounds,
                        resolvedSurface.Material.TerrainOverlay),
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor,
                    resolvedSurface.Material.TextureOffset))
            .OrderBy(static group => group.Min(static surface => ParsedSurfaceStableSortKey.Create(surface.Surface)), StringComparer.Ordinal)
            .Select((group, materialIndex) =>
            {
                ResolvedSurfaceMaterial representativeSurface = group.First();
                string terrainMaterialMeshCodeSource = TerrainOverlayMeshCodeResolver.ResolveMaterialMeshCodeSource(
                    cityObject.ActualMeshCode,
                    requestedMeshCode,
                    requestedMeshCodeBounds,
                    representativeSurface.Material.TerrainOverlay);
                return CreateMaterialBinding(
                    terrainMaterialMeshCodeSource,
                    representativeSurface,
                    materialIndex);
            })
            .ToArray();
    }

    private static MaterialBinding CreateMaterialBinding(
        string actualMeshCode,
        ResolvedSurfaceMaterial representativeSurface,
        int materialIndex)
    {
        string? terrainMeshCode = representativeSurface.Material.TerrainOverlay is null
            ? null
            : TerrainOverlayMeshCodeResolver.ResolveMeshCode(actualMeshCode, representativeSurface.Material.TerrainOverlay)
                ?? throw CreateTerrainOverlayMeshCodeMismatchException(
                    "material-binding",
                    actualMeshCode,
                    actualMeshCode,
                    requestedMeshCodeBounds: null,
                    representativeSurface.Material.TerrainOverlay);
        ColorRgba baseColor = representativeSurface.Material.TerrainOverlay is null
            ? ToContractColor(representativeSurface.Surface.BaseColor)
            : new ColorRgba(1.0, 1.0, 1.0, 1.0);
        DefaultCommonMaterialMember? commonMaterial = DefaultCommonMaterialAssignment.Resolve(
            baseColor,
            representativeSurface.Material.MaterialType,
            representativeSurface.Material.TexturePayload,
            representativeSurface.Material.TextureSourceKind,
            representativeSurface.Material.Projection,
            representativeSurface.DepthOffset,
            representativeSurface.Material.TextureScale,
            representativeSurface.Material.TextureOffset,
            representativeSurface.Material.TerrainOverlay,
            representativeSurface.Material.CommonMaterial);
        return new MaterialBinding(
            BaseColor: baseColor,
            MaterialType: representativeSurface.Material.MaterialType,
            TexturePayload: representativeSurface.Material.TexturePayload is null
                ? null
                : representativeSurface.Material.TexturePayload,
            TextureSourceKind: representativeSurface.Material.TextureSourceKind,
            Projection: representativeSurface.Material.Projection,
            DepthOffset: representativeSurface.DepthOffset is null
                ? null
                : representativeSurface.DepthOffset,
            SubmeshIndices: [materialIndex],
            TextureScale: representativeSurface.Material.TextureScale is null
                ? null
                : representativeSurface.Material.TextureScale,
            Family: representativeSurface.Material.Family,
            TextureOffset: representativeSurface.Material.TextureOffset is null
                ? null
                : representativeSurface.Material.TextureOffset,
            ReuseScope: representativeSurface.Material.ReuseScope,
            TerrainOverlay: representativeSurface.Material.TerrainOverlay,
            BundledVariantIndex: representativeSurface.Material.BundledVariantIndex,
            TerrainMeshCode: terrainMeshCode,
            CommonMaterial: commonMaterial);
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

    private static Float2 ToContractFloat2(ScalarPair value) => new(value.X, value.Y);

    private static ColorRgba ToInternalColor(ColorRgba value) => new(value.R, value.G, value.B, value.A);

    private static Float3 ToContractFloat3(Float3 value) => new(value.X, value.Y, value.Z);

    private static Quaternion ToContractQuaternion(Quaternion value) => new(value.X, value.Y, value.Z, value.W);

    private static ColorRgba ToContractColor(ColorRgba value) => new(value.R, value.G, value.B, value.A);

    private static TextureUvRect? TryCreateDemTerrainGridOccupiedUvRect(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        if (demTerrainTextureOverlay is null)
        {
            return null;
        }

        GeographicRectangle? demObjectBounds = TryGetDemObjectGeographicBounds(cityObject, demTerrainTextureOverlay);
        double cityObjectMinAltitude = ResolveProjectionMinimumAltitude(cityObject.Surfaces);
        ResolvedSurfaceMaterial? representativeSurface = cityObject.Surfaces
            .Select(surface => ResolveSurfaceMaterial(
                cityObject,
                cityObjectOrigin,
                cityObjectCartesian,
                surface,
                cityObjectMinAltitude,
                demTerrainTextureOverlay,
                materialResolver))
            .FirstOrDefault(static resolvedSurface => resolvedSurface.Surface.UsesGeneratedDemTexture);
        if (representativeSurface is null)
        {
            return null;
        }

        TextureUvRect? occupiedUvRect = DemTerrainOverlayAssignment.TryCreateTerrainGridOccupiedUvRect(
            global::PlateauResoniteLink.Application.Importing.CityGmlProjectionModelAdapter.FromProjectionModel(cityObject),
            representativeSurface,
            demTerrainTextureOverlay,
            demObjectBounds);
        return occupiedUvRect is { IsIdentity: true } ? null : occupiedUvRect;
    }

    private static GeographicRectangle? TryGetDemObjectGeographicBounds(
        ParsedCityObject cityObject,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        if (demTerrainTextureOverlay is null
            || !string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ResolveProjectionCityObjectGeographicBounds(cityObject);
    }

    private static DemTerrainGridBounds CreateDemTerrainGridBounds(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        Float3 slotPosition,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IReadOnlyList<Float3> positions)
    {
        double rawMinX = positions.Min(static position => position.X);
        double rawMaxX = positions.Max(static position => position.X);
        double rawMinZ = positions.Min(static position => position.Z);
        double rawMaxZ = positions.Max(static position => position.Z);

        if (demTerrainTextureOverlay is null)
        {
            return new DemTerrainGridBounds(rawMinX, rawMaxX, rawMinZ, rawMaxZ);
        }

        GeographicRectangle clippedBounds = IntersectGeographicBounds(
            ResolveProjectionCityObjectGeographicBounds(cityObject),
            demTerrainTextureOverlay.GeographicBounds);
        double referenceLatitude = cityObjectOrigin.Latitude;
        double referenceLongitude = cityObjectOrigin.Longitude;
        Float3 westPosition = CreateGlobalTerrainGridLocalPosition(
            new GeodeticPoint(referenceLatitude, clippedBounds.MinLongitude, cityObjectOrigin.Altitude),
            slotPosition,
            globalOriginPoint,
            globalCartesian);
        Float3 eastPosition = CreateGlobalTerrainGridLocalPosition(
            new GeodeticPoint(referenceLatitude, clippedBounds.MaxLongitude, cityObjectOrigin.Altitude),
            slotPosition,
            globalOriginPoint,
            globalCartesian);
        Float3 southPosition = CreateGlobalTerrainGridLocalPosition(
            new GeodeticPoint(clippedBounds.MinLatitude, referenceLongitude, cityObjectOrigin.Altitude),
            slotPosition,
            globalOriginPoint,
            globalCartesian);
        Float3 northPosition = CreateGlobalTerrainGridLocalPosition(
            new GeodeticPoint(clippedBounds.MaxLatitude, referenceLongitude, cityObjectOrigin.Altitude),
            slotPosition,
            globalOriginPoint,
            globalCartesian);

        double clippedMinX = Math.Min(westPosition.X, eastPosition.X);
        double clippedMaxX = Math.Max(westPosition.X, eastPosition.X);
        double clippedMinZ = Math.Min(southPosition.Z, northPosition.Z);
        double clippedMaxZ = Math.Max(southPosition.Z, northPosition.Z);

        if ((clippedMaxX - clippedMinX) <= 1e-6 || (clippedMaxZ - clippedMinZ) <= 1e-6)
        {
            return new DemTerrainGridBounds(rawMinX, rawMaxX, rawMinZ, rawMaxZ);
        }

        return new DemTerrainGridBounds(clippedMinX, clippedMaxX, clippedMinZ, clippedMaxZ);
    }

    private static GeographicRectangle ResolveProjectionCityObjectGeographicBounds(ParsedCityObject cityObject)
    {
        return CityObjectGeographicBoundsResolver.Resolve(
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices),
            static point => point.Latitude,
            static point => point.Longitude);
    }

    private static GeographicRectangle IntersectGeographicBounds(
        GeographicRectangle left,
        GeographicRectangle right)
    {
        return new GeographicRectangle(
            MinLatitude: Math.Max(left.MinLatitude, right.MinLatitude),
            MaxLatitude: Math.Min(left.MaxLatitude, right.MaxLatitude),
            MinLongitude: Math.Max(left.MinLongitude, right.MinLongitude),
            MaxLongitude: Math.Min(left.MaxLongitude, right.MaxLongitude));
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

    private static TerrainGridTriangle[] CreateDemTerrainGridTriangles(
        ParsedCityObject cityObject,
        Float3 slotPosition,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian)
    {
        List<TerrainGridTriangle> triangles = [];
        foreach (ParsedSurface surface in cityObject.Surfaces)
        {
            Float3[] positions = surface.ExteriorRing.Vertices
                .Select(point => CreateGlobalTerrainGridLocalPosition(point, slotPosition, globalOriginPoint, globalCartesian))
                .ToArray();
            if (positions.Length < 3)
            {
                continue;
            }

            Float3 origin = positions[0];
            for (int index = 1; index + 1 < positions.Length; index++)
            {
                triangles.Add(new TerrainGridTriangle(origin, positions[index], positions[index + 1]));
            }
        }

        return triangles.ToArray();
    }

    private static Float3 CreateGlobalTerrainGridLocalPosition(
        GeodeticPoint point,
        Float3 slotPosition,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian)
    {
        Float3 globalPosition = CreateScenePosition(point, globalOriginPoint, globalCartesian);
        return new Float3(
            globalPosition.X - slotPosition.X,
            globalPosition.Y - slotPosition.Y,
            globalPosition.Z - slotPosition.Z);
    }

    internal static void ValidateCompatibleReferenceSystem(
        CoordinateReferenceSystem expectedReferenceSystem,
        CoordinateReferenceSystem? actualReferenceSystem)
    {
        if (actualReferenceSystem is null || expectedReferenceSystem.IsCompatibleWith(actualReferenceSystem))
        {
            return;
        }

        throw new PlateauImportValidationException(
            [$"Mixed CityGML coordinate reference systems are not supported. Found '{expectedReferenceSystem.SrsName}' and '{actualReferenceSystem.SrsName}'."]);
    }

    internal sealed record ParsedCityObject(
        string SlotKey,
        string DisplayName,
        string PackageName,
        string ActualMeshCode,
        int? LodLevel,
        ParsedSurface[] Surfaces,
        CoordinateReferenceSystem ReferenceSystem,
        string SourceFileRelativePath,
        bool SharedAcrossMeshCodes,
        bool TerrainAligned = false,
        GeodeticPoint? GeodeticOriginOverride = null,
        int? FloorsAboveGround = null,
        double? MeasuredHeightMeters = null,
        BuildingAttributeContext? BuildingAttributes = null,
        double? GeometryHeightMeters = null);

    internal sealed record SourceFileDescriptor(
        string RelativePath,
        string PackageName,
        string MatchedMeshCode,
        bool RequiresMeshCodeBoundsFilter);

    internal sealed record CachedSourceFileDescriptor(
        SourceFileDescriptor SourceFile,
        ParsedCityObject[] CityObjects)
    {
        public string RelativePath => SourceFile.RelativePath;

        public string PackageName => SourceFile.PackageName;
    }

    internal sealed class SourceFilePipeline
    {
        private readonly object parseTaskGate = new();
        private readonly Func<Task<ParsedSourceFileResult>> parseTaskFactory;
        private Task<ParsedSourceFileResult>? parseTask;

        public SourceFilePipeline(
            SourceFileDescriptor sourceFile,
            Func<Task<ParsedSourceFileResult>> parseTaskFactory)
        {
            SourceFile = sourceFile;
            this.parseTaskFactory = parseTaskFactory;
        }

        public SourceFileDescriptor SourceFile { get; }

        public Task<ParsedSourceFileResult> GetParseTask()
        {
            lock (parseTaskGate)
            {
                parseTask ??= parseTaskFactory();
                return parseTask;
            }
        }
    }

    internal sealed record ParsedSourceFileResult(
        SourceFileDescriptor SourceFile,
        ParsedCityObject[] CityObjects,
        CoordinateReferenceSystem? ReferenceSystem,
        ProjectionTerrainHeightTriangle[] TerrainTriangles,
        TimeSpan Elapsed);

    internal sealed record ParsedRing(
        string RingId,
        GeodeticPoint[] Vertices,
        IReadOnlyList<Float2>? UVs);

    private sealed record DemTerrainGridBounds(
        double MinX,
        double MaxX,
        double MinZ,
        double MaxZ);

    internal sealed record ParsedSurface(
        string PolygonId,
        ParsedSurfaceSemantic Semantic,
        ParsedRing ExteriorRing,
        ParsedRing[] InteriorRings,
        ColorRgba BaseColor,
        TexturePayload? TexturePayload,
        bool UsesGeneratedDemTexture = false,
        MaterialOpticalProperties? OpticalProperties = null)
    {
        public IEnumerable<GeodeticPoint> Vertices =>
            ExteriorRing.Vertices.Concat(InteriorRings.SelectMany(static ring => ring.Vertices));
    }

    internal enum ParsedSurfaceSemantic
    {
        Unknown = 0,
        Wall = 1,
        Roof = 2,
        Ground = 3,
        Closure = 4,
        OuterCeiling = 5,
        OuterFloor = 6,
    }

    internal sealed record GeodeticPoint(
        double Latitude,
        double Longitude,
        double Altitude);

    internal sealed record CoordinateReferenceSystem(
        string SrsName,
        Geocentric? Geocentric,
        string CompatibilityKey)
    {
        public bool IsGeographic => Geocentric is not null;

        public bool IsCompatibleWith(CoordinateReferenceSystem other)
        {
            ArgumentNullException.ThrowIfNull(other);

            return string.Equals(CompatibilityKey, other.CompatibilityKey, StringComparison.Ordinal);
        }

        public static CoordinateReferenceSystem Parse(XDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            string? srsName = document
                .Descendants(Gml + "Envelope")
                .Attributes("srsName")
                .Select(static attribute => attribute.Value.Trim())
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

            return Parse(srsName);
        }

        public static CoordinateReferenceSystem Parse(string? srsName)
        {
            if (string.IsNullOrWhiteSpace(srsName))
            {
                return new CoordinateReferenceSystem("local-cartesian", null, "local-cartesian");
            }

            (Geocentric geocentric, string compatibilityKey) = ResolveGeocentric(srsName);
            return new CoordinateReferenceSystem(srsName, geocentric, compatibilityKey);
        }

        private static (Geocentric Geocentric, string CompatibilityKey) ResolveGeocentric(string srsName)
        {
            if (srsName.EndsWith("/6697", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:6697", StringComparison.OrdinalIgnoreCase)
                || srsName.EndsWith("/6668", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:6668", StringComparison.OrdinalIgnoreCase))
            {
                return (new Geocentric(Ellipsoid.GRS80), "jgd2011");
            }

            if (srsName.EndsWith("/6696", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:6696", StringComparison.OrdinalIgnoreCase))
            {
                return (new Geocentric(Ellipsoid.GRS80), "jgd2011");
            }

            if (srsName.EndsWith("/4979", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:4979", StringComparison.OrdinalIgnoreCase)
                || srsName.EndsWith("/4326", StringComparison.Ordinal)
                || srsName.EndsWith("EPSG:4326", StringComparison.OrdinalIgnoreCase))
            {
                return (Geocentric.WGS84, "wgs84");
            }

            throw new PlateauImportValidationException(
                [$"Unsupported CityGML CRS '{srsName}'. Only geographic 3D CRS values currently used by PLATEAU are supported."]);
        }
    }

}
