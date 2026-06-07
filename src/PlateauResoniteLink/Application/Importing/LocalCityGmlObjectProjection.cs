using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using PlateauResoniteLink.Domain.Importing;

using LocalCartesian = GeographicLib.LocalCartesian;

namespace PlateauResoniteLink.Application.Importing;

internal static class LocalCityGmlObjectProjection
{
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

    internal static TerrainTextureOverlay[] CreateDemTerrainTextureOverlays(
        MeshCodeBounds demBounds,
        IReadOnlyList<string> requestedMeshCodes)
    {
        return DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(
            DemTerrainBounds.FromMeshCodeBounds(demBounds),
            requestedMeshCodes)
            .Select(static region => DemTerrainTextureDefaults.CreatePlateauOrthoWithGsiFallbackOverlay(region.MeshCode, region.GeographicBounds))
            .ToArray();
    }

    internal static ImportedCityObject ProjectCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        ParsedCityObject heightResolvedCityObject = cityObject.GeometryHeightMeters.HasValue
            ? cityObject
            : cityObject with
            {
                GeometryHeightMeters = CityObjectAltitudeMetricsResolver.TryGetGeometryHeightMeters(
                    cityObject.Surfaces.SelectMany(static surface => surface.Vertices)),
            };
        return CityGmlTriangleMeshCityObjectProjection.Project(
            GeneratedLod1RoofCityObjectFactory.CreateDraft(heightResolvedCityObject),
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            materialResolver);
    }

    internal static IEnumerable<ImportedCityObject> ProjectCityObjects(
        global::PlateauResoniteLink.Application.Importing.CachedSourceFileDescriptor sourceFile,
        global::PlateauResoniteLink.Application.Importing.CoordinateReferenceSystem referenceSystem,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        IReadOnlyList<string> selectedMeshCodes,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver,
        Func<global::PlateauResoniteLink.Application.Importing.ParsedCityObject, bool>? predicate = null,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return CityGmlParsedCityObjectProjection.ProjectSourceFile(
            sourceFile,
            referenceSystem,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlays,
            requestedMeshCodeBounds,
            selectedMeshCodes,
            request,
            materialResolver,
            predicate,
            progressReporter,
            cancellationToken);
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
        return CityGmlParsedCityObjectProjection.Project(
            parsedCityObject,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlays,
            requestedMeshCodeBounds,
            terrainHeightSampler,
            request,
            materialResolver,
            progressReporter,
            cancellationToken);
    }

    internal static IEnumerable<MaterialBinding> EnumerateCommonMaterialsForParsedCityObject(
        global::PlateauResoniteLink.Application.Importing.ParsedCityObject parsedCityObject,
        global::PlateauResoniteLink.Application.Importing.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        ProjectionTerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver)
    {
        return CityGmlParsedCityObjectProjection.EnumerateCommonMaterials(
            parsedCityObject,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlays,
            requestedMeshCodeBounds,
            terrainHeightSampler,
            request,
            materialResolver);
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

}
