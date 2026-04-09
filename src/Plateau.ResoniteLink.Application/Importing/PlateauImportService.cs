using System.Diagnostics;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class PlateauImportService(
    IResoniteSceneBuilder sceneBuilder,
    IPlateauDatasetSourceResolver? datasetSourceResolver = null,
    Action<string>? progressReporter = null,
    IResoniteConstructionSourceFactory? constructionSourceFactory = null)
{
    private readonly IResoniteSceneBuilder sceneBuilder = sceneBuilder;
    private readonly IPlateauDatasetSourceResolver datasetSourceResolver =
        datasetSourceResolver ?? new CkanPlateauDatasetSourceResolver();
    private readonly Action<string>? progressReporter = progressReporter;
    private readonly IResoniteConstructionSourceFactory constructionSourceFactory =
        constructionSourceFactory ?? new LocalCityGmlConstructionSourceFactory();

    public async Task<ImportExecutionResult> ExecuteAsync(
        PlateauImportRequest request,
        string workRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        PlateauImportRequest validationRequest = NormalizeRequestForValidation(request);
        IReadOnlyList<string> validationErrors = PlateauImportRequestValidator.Validate(validationRequest);
        if (validationErrors.Count > 0)
        {
            throw new PlateauImportValidationException(validationErrors);
        }

        PlateauImportRequest normalizedRequest = NormalizeRequest(validationRequest);

        PlateauImportRequest resolvedRequest =
            await datasetSourceResolver.ResolveAsync(normalizedRequest, workRoot, cancellationToken);
        ReportProgress(
            $"[import] Resolved dataset source for '{resolvedRequest.Dataset}' mesh '{resolvedRequest.MeshCode}'.");

        Stopwatch sourceStopwatch = Stopwatch.StartNew();
        IResoniteConstructionSource source = await constructionSourceFactory.CreateAsync(
            resolvedRequest,
            progressReporter,
            cancellationToken);
        sourceStopwatch.Stop();
        ReportProgress(
            $"[import] Prepared construction source in {sourceStopwatch.Elapsed.TotalSeconds:F3}s.");

        try
        {
            Stopwatch beginStopwatch = Stopwatch.StartNew();
            await sceneBuilder.BeginAsync(source.Metadata, workRoot, cancellationToken);
            beginStopwatch.Stop();
            ReportProgress($"[import] Scene builder initialization completed in {beginStopwatch.Elapsed.TotalSeconds:F3}s.");

            bool processedAnyCityObject = false;
            int processedCityObjectCount = 0;
            Stopwatch cityObjectStopwatch = Stopwatch.StartNew();

            await foreach (ResoniteConstructionCityObject cityObject in source.ReadCityObjectsAsync(cancellationToken))
            {
                processedAnyCityObject = true;
                await sceneBuilder.ProcessCityObjectAsync(cityObject, cancellationToken);
                processedCityObjectCount++;
            }

            cityObjectStopwatch.Stop();
            ReportProgress(
                $"[import] Streamed {processedCityObjectCount} city objects in {cityObjectStopwatch.Elapsed.TotalSeconds:F3}s.");

            if (!processedAnyCityObject)
            {
                throw new PlateauImportValidationException(
                    [$"No triangulated CityGML geometry was produced for mesh code '{resolvedRequest.MeshCode}'."]);
            }

            Stopwatch completeStopwatch = Stopwatch.StartNew();
            IReadOnlyList<string> destinations = await sceneBuilder.CompleteAsync(cancellationToken);
            completeStopwatch.Stop();
            ReportProgress(
                $"[import] Scene builder completion finished in {completeStopwatch.Elapsed.TotalSeconds:F3}s.");
            return new ImportExecutionResult(source.Metadata, destinations);
        }
        finally
        {
            await sceneBuilder.DisposeAsync();
        }
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }

    private static PlateauImportRequest NormalizeRequestForValidation(PlateauImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request with
        {
            Dataset = TrimToEmpty(request.Dataset),
            MeshCode = TrimToEmpty(request.MeshCode),
            LocalSourcePath = string.IsNullOrWhiteSpace(request.LocalSourcePath) ? null : request.LocalSourcePath.Trim(),
            PackageNames = request.PackageNames is null
                ? null
                : request.PackageNames.Select(static packageName => TrimToEmpty(packageName)).ToArray(),
            ExcludeLodLevelsByPackage = request.ExcludeLodLevelsByPackage is null
                ? null
                : request.ExcludeLodLevelsByPackage.ToDictionary(
                    static pair => pair.Key.Trim(),
                    static pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase),
            PackagePatterns = request.PackagePatterns is null
                ? null
                : request.PackagePatterns.ToDictionary(
                    static pair => pair.Key.Trim(),
                    static pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase),
        };
    }

    private static string TrimToEmpty(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static PlateauImportRequest NormalizeRequest(PlateauImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request with
        {
            PackageNames = request.PackageNames is null
                ? null
                : PlateauPackageCatalog.NormalizeRequestedPackageNames(request.PackageNames),
            ExcludeLodLevelsByPackage = request.ExcludeLodLevelsByPackage is null
                ? null
                : NormalizePackageExclusionMap(request.ExcludeLodLevelsByPackage),
            PackagePatterns = request.PackagePatterns is null
                ? null
                : NormalizePackagePatternMap(request.PackagePatterns),
        };
    }

    private static Dictionary<string, IReadOnlySet<int>> NormalizePackageExclusionMap(
        IReadOnlyDictionary<string, IReadOnlySet<int>> exclusionsByPackage)
    {
        Dictionary<string, IReadOnlySet<int>> normalized = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string packageName, IReadOnlySet<int> excludedLods) in exclusionsByPackage)
        {
            if (!PlateauPackageCatalog.TryNormalizePackageName(packageName, out string normalizedPackageName))
            {
                throw new ArgumentException(
                    $"Unsupported package '{packageName}'. Supported packages: {string.Join(", ", PlateauPackageCatalog.SupportedPackageNames)}.");
            }

            normalized[normalizedPackageName] = excludedLods;
        }

        return normalized;
    }

    private static Dictionary<string, string> NormalizePackagePatternMap(
        IReadOnlyDictionary<string, string> patternsByPackage)
    {
        Dictionary<string, string> normalized = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string packageName, string pattern) in patternsByPackage)
        {
            if (!PlateauPackageCatalog.TryNormalizePackageName(packageName, out string normalizedPackageName))
            {
                throw new ArgumentException(
                    $"Unsupported package '{packageName}'. Supported packages: {string.Join(", ", PlateauPackageCatalog.SupportedPackageNames)}.");
            }

            normalized[normalizedPackageName] = pattern;
        }

        return normalized;
    }
}
