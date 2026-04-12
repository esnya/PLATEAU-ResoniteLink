using System.Diagnostics;
using System.Xml.Linq;

using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public static partial class LocalCityGmlResonitePlanBuilder
{
    public static Task<IResoniteConstructionSource> CreateConstructionSourceAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return CreateConstructionSourceFactory().CreateAsync(
            request,
            progressReporter,
            cancellationToken);
    }

    public static IResoniteConstructionSource CreateConstructionSource(
        PlateauImportRequest request,
        Action<string>? progressReporter = null)
    {
        return CreateConstructionSourceAsync(request, progressReporter).GetAwaiter().GetResult();
    }

    private static IResoniteConstructionSourceFactory CreateConstructionSourceFactory()
    {
        return PlateauImportApplicationComposition.CreateConstructionSourceFactory();
    }

    internal static ResoniteLocalOrigin? ResolveLocalOrigin(
        MeshCodeArea? requestedMeshArea)
    {
        return requestedMeshArea?.GetCenter();
    }

    internal static async Task<SourceFilePipeline[]> CreateSourceFilePipelinesAsync(
        IReadOnlyList<SourceFileDescriptor> sourceFiles,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeArea> requestedMeshAreas,
        Action<string>? progressReporter,
        LodFilteringStrategy lodFilteringStrategy,
        CancellationToken cancellationToken)
    {
        return sourceFiles
            .Select(sourceFile =>
                new SourceFilePipeline(
                    sourceFile,
                    () => ParseSourceFileAsync(
                        sourceFile,
                        datasetSource,
                        requestedMeshAreas,
                        progressReporter,
                        lodFilteringStrategy,
                        cancellationToken)))
            .ToArray();
    }

    internal static async Task<ParsedSourceFileResult> ParseSourceFileAsync(
        SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeArea> requestedMeshAreas,
        Action<string>? progressReporter,
        LodFilteringStrategy lodFilteringStrategy,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Stopwatch fileStopwatch = Stopwatch.StartNew();
        List<ParsedCityObject> cityObjects = [];
        await foreach (ParsedCityObject cityObject in StreamParsedCityObjectsAsync(
                           sourceFile,
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
        CoordinateReferenceSystem coordinateReferenceSystem = await ReadDocumentReferenceSystemAsync(
            datasetSource,
            sourceFile.RelativePath,
            cancellationToken);
        TerrainHeightTriangle[] terrainTriangles = string.Equals(sourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            ? [.. ExtractTerrainHeightTriangles(cityObjectArray)]
            : [];

        progressReporter?.Invoke(
            PlateauLog.Debug(
                "import",
                $"Parsed file '{sourceFile.RelativePath}' "
                + $"({sourceFile.PackageName}, {cityObjectArray.Length} city objects) "
                + $"in {fileStopwatch.Elapsed.TotalSeconds:F3}s."));

        return new ParsedSourceFileResult(
            sourceFile,
            cityObjectArray,
            coordinateReferenceSystem,
            terrainTriangles,
            fileStopwatch.Elapsed);
    }

    internal static async IAsyncEnumerable<ParsedCityObject> StreamParsedCityObjectsAsync(
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

    internal static async Task<CoordinateReferenceSystem> ReadDocumentReferenceSystemAsync(
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
