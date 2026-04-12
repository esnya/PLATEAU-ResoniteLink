using System.Diagnostics;
using System.Xml.Linq;

using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public static partial class LocalCityGmlResonitePlanBuilder
{
    internal static ResoniteLocalOrigin? ResolveLocalOrigin(
        MeshCodeArea? requestedMeshArea)
    {
        return requestedMeshArea?.GetCenter();
    }

    internal static async Task<LocalCityGmlDocumentSet> ReadDocumentSetAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return await LocalCityGmlBootstrapPipeline.ReadDocumentSetCoreAsync(
            request,
            progressReporter,
            cancellationToken);
    }

    internal static Task<global::Plateau.ResoniteLink.Application.Importing.SourceFilePipeline[]> CreateSourceFilePipelinesCoreAsync(
        IReadOnlyList<global::Plateau.ResoniteLink.Application.Importing.SourceFileDescriptor> sourceFiles,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeArea> requestedMeshAreas,
        Action<string>? progressReporter,
        LodFilteringStrategy lodFilteringStrategy,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            sourceFiles
                .Select(sourceFile =>
                    new global::Plateau.ResoniteLink.Application.Importing.SourceFilePipeline(
                        sourceFile,
                        () => ParseSourceFileCoreAsync(
                            sourceFile,
                            datasetSource,
                            requestedMeshAreas,
                            progressReporter,
                            lodFilteringStrategy,
                            cancellationToken)))
                .ToArray());
    }

    internal static async Task<global::Plateau.ResoniteLink.Application.Importing.ParsedSourceFileResult> ParseSourceFileCoreAsync(
        global::Plateau.ResoniteLink.Application.Importing.SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeArea> requestedMeshAreas,
        Action<string>? progressReporter,
        LodFilteringStrategy lodFilteringStrategy,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Stopwatch fileStopwatch = Stopwatch.StartNew();
        List<ParsedCityObject> cityObjects = [];
        await foreach (ParsedCityObject cityObject in StreamParsedCityObjectsCoreAsync(
                           sourceFile.ToLegacy(),
                           datasetSource,
                           requestedMeshAreas,
                           lodFilteringStrategy,
                           cancellationToken))
        {
            cityObjects.Add(cityObject);
        }
        fileStopwatch.Stop();

        ParsedCityObject[] cityObjectArray = cityObjects
            .OrderBy(static cityObject => cityObject.SlotKey, StringComparer.Ordinal)
            .ToArray();
        CoordinateReferenceSystem coordinateReferenceSystem = await ReadDocumentReferenceSystemCoreAsync(
            datasetSource,
            sourceFile.RelativePath,
            cancellationToken);
        global::Plateau.ResoniteLink.Application.Importing.TerrainHeightTriangle[] terrainTriangles = string.Equals(sourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            ? LocalCityGmlDemBootstrapSupport.CreateTerrainHeightTriangles(cityObjectArray.Select(BootstrapParsedCityObject.FromLegacy))
            : [];

        progressReporter?.Invoke(
            PlateauLog.Debug(
                "import",
                $"Parsed file '{sourceFile.RelativePath}' "
                + $"({sourceFile.PackageName}, {cityObjectArray.Length} city objects) "
                + $"in {fileStopwatch.Elapsed.TotalSeconds:F3}s."));

        return new global::Plateau.ResoniteLink.Application.Importing.ParsedSourceFileResult(
            sourceFile,
            cityObjectArray.Select(BootstrapParsedCityObject.FromLegacy).ToArray(),
            global::Plateau.ResoniteLink.Application.Importing.CoordinateReferenceSystem.FromLegacy(coordinateReferenceSystem),
            terrainTriangles,
            fileStopwatch.Elapsed);
    }

    internal static async IAsyncEnumerable<ParsedCityObject> StreamParsedCityObjectsCoreAsync(
        SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeArea> requestedMeshAreas,
        LodFilteringStrategy? lodFilteringStrategy,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        lodFilteringStrategy ??= new LodFilteringStrategy();
        await using Stream stream = await datasetSource.OpenReadAsync(sourceFile.RelativePath, cancellationToken);
        XDocument document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        ParsedCityObject[] cityObjects = ParseCityObjects(
            document,
            sourceFile,
            datasetSource,
            requestedMeshAreas,
            lodFilteringStrategy);

        foreach (ParsedCityObject cityObject in cityObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return cityObject;
        }
    }

    internal static async Task<CoordinateReferenceSystem> ReadDocumentReferenceSystemCoreAsync(
        IPlateauDatasetContentSource datasetSource,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await datasetSource.OpenReadAsync(relativePath, cancellationToken);
        XDocument document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        try
        {
            return CoordinateReferenceSystem.Parse(document);
        }
        catch (PlateauImportValidationException)
        {
            throw new PlateauImportValidationException(
                [$"CityGML file '{NormalizePath(relativePath)}' does not declare a supported coordinate reference system."]);
        }
    }
}
