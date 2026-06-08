using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Application.Importing;

internal static class TestCityGmlGeometryProjector
{
    public static CityGmlGeometryProjector Create()
    {
        ResolveDefaultMaterial materialResolver =
            new DefaultMaterialResolver(CommonMaterialCatalog.Create()).ResolveMaterial;
        return (
            sourceFile,
            referenceSystem,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlays,
            requestedMeshCodeBounds,
            request,
            predicate,
            progressReporter,
            cancellationToken) => LocalCityGmlObjectProjection.ProjectCityObjects(
                sourceFile,
                referenceSystem,
                globalOriginPoint,
                globalCartesian,
                demTerrainTextureOverlays,
                requestedMeshCodeBounds,
                request,
                materialResolver,
                predicate,
                progressReporter,
                cancellationToken);
    }
}
