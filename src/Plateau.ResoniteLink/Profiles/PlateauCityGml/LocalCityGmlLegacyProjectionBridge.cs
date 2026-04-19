using GeographicLib;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class LocalCityGmlLegacyProjectionBridge : ICityGmlLegacyProjectionBridge
{
    public IEnumerable<ResoniteConstructionCityObject> MaterializeCityObjects(
        CachedSourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        IDefaultMaterialResolver materialResolver,
        Func<BootstrapParsedCityObject, bool>? predicate = null)
    {
        return LocalCityGmlObjectProjection.MaterializeCityObjects(
            sourceFile.ToLegacy(),
            referenceSystem.ToLegacy(),
            globalOriginPoint.ToLegacy(),
            globalCartesian,
            demTerrainTextureOverlays,
            requestedMeshAreas,
            terrainHeightSampler?.ToLegacy(),
            request,
            materialResolver,
            predicate is null ? null : cityObject => predicate(BootstrapParsedCityObject.FromLegacy(cityObject)));
    }

    public IEnumerable<ResoniteMaterialBinding> EnumerateCommonMaterials(
        CachedSourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        ISet<string>? emittedMaterialKeys = null)
    {
        return LocalCityGmlObjectProjection.EnumerateCommonMaterials(
            sourceFile.ToLegacy(),
            referenceSystem.ToLegacy(),
            globalOriginPoint.ToLegacy(),
            globalCartesian,
            demTerrainTextureOverlays,
            terrainHeightSampler?.ToLegacy(),
            request,
            emittedMaterialKeys);
    }

    internal static BootstrapParsedCityObject ToBootstrapParsedCityObject(LocalCityGmlObjectProjection.ParsedCityObject cityObject)
    {
        return BootstrapParsedCityObject.FromLegacy(cityObject);
    }

    internal static ParsedSourceFileResult ToParsedSourceFileResult(
        SourceFileDescriptor sourceFile,
        BootstrapParsedCityObject[] cityObjects,
        LocalCityGmlObjectProjection.CoordinateReferenceSystem? referenceSystem,
        TerrainHeightTriangle[] terrainTriangles,
        TimeSpan elapsed)
    {
        return new ParsedSourceFileResult(
            sourceFile,
            cityObjects,
            referenceSystem is null ? null : CoordinateReferenceSystem.FromLegacy(referenceSystem),
            terrainTriangles,
            elapsed);
    }

    internal static ParsedSourceFileResult ToParsedSourceFileResult(LocalCityGmlObjectProjection.ParsedSourceFileResult sourceFile)
    {
        return new ParsedSourceFileResult(
            SourceFileDescriptor.FromLegacy(sourceFile.SourceFile),
            sourceFile.CityObjects.Select(ToBootstrapParsedCityObject).ToArray(),
            sourceFile.ReferenceSystem is null ? null : CoordinateReferenceSystem.FromLegacy(sourceFile.ReferenceSystem),
            sourceFile.TerrainTriangles.Select(TerrainHeightTriangle.FromLegacy).ToArray(),
            sourceFile.Elapsed);
    }

    internal static TerrainTextureOverlay[] CreateDemTerrainTextureOverlays(
        MeshCodeBounds demBounds,
        IReadOnlyList<string> requestedMeshCodes)
    {
        return LocalCityGmlDemBootstrapSupport.CreateDemTerrainTextureOverlays(
            DemTerrainBounds.FromLegacy(demBounds),
            requestedMeshCodes);
    }

    internal static MeshCodeBounds? ResolveDemTerrainBounds(
        IEnumerable<LocalCityGmlObjectProjection.ParsedSourceFileResult> demParsedSourceFiles,
        MeshCodeBounds? fallbackBounds)
    {
        DemTerrainBounds? bounds = LocalCityGmlDemBootstrapSupport.ResolveDemTerrainBounds(
            demParsedSourceFiles.Select(LocalCityGmlLegacyProjectionBridge.ToParsedSourceFileResult),
            fallbackBounds is null ? null : DemTerrainBounds.FromLegacy(fallbackBounds));
        return bounds?.ToLegacy();
    }

    internal static LocalCityGmlObjectProjection.TerrainHeightTriangle[] ExtractTerrainHeightTriangles(
        IEnumerable<LocalCityGmlObjectProjection.ParsedCityObject> cityObjects)
    {
        return LocalCityGmlDemBootstrapSupport.CreateTerrainHeightTriangles(
                cityObjects.Select(LocalCityGmlLegacyProjectionBridge.ToBootstrapParsedCityObject))
            .Select(static triangle => triangle.ToLegacy())
            .ToArray();
    }
}
