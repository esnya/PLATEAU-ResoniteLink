using System.Diagnostics;

using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class PlateauImportService(
    IResoniteSceneBuilder sceneBuilder,
    IPlateauDatasetSourceResolver datasetSourceResolver,
    IResoniteConstructionSourceFactory constructionSourceFactory,
    Action<string>? progressReporter = null)
{
    private readonly IResoniteSceneBuilder sceneBuilder =
        sceneBuilder ?? throw new ArgumentNullException(nameof(sceneBuilder));
    private readonly IPlateauDatasetSourceResolver datasetSourceResolver =
        datasetSourceResolver ?? throw new ArgumentNullException(nameof(datasetSourceResolver));
    private readonly Action<string>? progressReporter = progressReporter;
    private readonly IResoniteConstructionSourceFactory constructionSourceFactory =
        constructionSourceFactory ?? throw new ArgumentNullException(nameof(constructionSourceFactory));

    public async Task<ImportExecutionResult> ExecuteAsync(
        PlateauImportRequest request,
        string workRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        ValidatedPlateauImportRequest validatedRequest = PlateauImportRequestValidator.NormalizeAndValidateOrThrow(request);
        PlateauImportRequest normalizedRequest = validatedRequest.ToImportRequest();
        string datasetWorkRoot = WorkRootLayout.ResolveDatasetRoot(workRoot, validatedRequest.Dataset);

        PlateauImportRequest resolvedRequest =
            (await datasetSourceResolver.ResolveAsync(validatedRequest, datasetWorkRoot, cancellationToken)).ToImportRequest();
        ReportProgress(
            PlateauLog.Debug("import", $"Resolved dataset source for '{resolvedRequest.Dataset}' mesh '{resolvedRequest.MeshCode}'."));

        Task commonMaterialPrewarmTask = Task.CompletedTask;
        CancellationTokenSource? commonMaterialPrewarmCancellation = null;
        try
        {
            Stopwatch connectStopwatch = Stopwatch.StartNew();
            await sceneBuilder.EnsureConnectedAsync(normalizedRequest, cancellationToken);
            connectStopwatch.Stop();
            ReportProgress(
                PlateauLog.Debug("import", $"Scene builder connection check completed in {connectStopwatch.Elapsed.TotalSeconds:F3}s."));

            Stopwatch sourceStopwatch = Stopwatch.StartNew();
            IResoniteConstructionSource source = await constructionSourceFactory.CreateAsync(
                resolvedRequest,
                progressReporter,
                cancellationToken);
            sourceStopwatch.Stop();
            ReportProgress(
                PlateauLog.Debug("import", $"Prepared construction source in {sourceStopwatch.Elapsed.TotalSeconds:F3}s."));

            Stopwatch beginStopwatch = Stopwatch.StartNew();
            ReportProgress(PlateauLog.Info("import", "Starting live scene initialization."));
            await sceneBuilder.BeginAsync(source.Metadata, datasetWorkRoot, cancellationToken);
            beginStopwatch.Stop();
            ReportProgress(PlateauLog.Debug("import", $"Scene builder initialization completed in {beginStopwatch.Elapsed.TotalSeconds:F3}s."));
            commonMaterialPrewarmCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            commonMaterialPrewarmTask = PrewarmCommonMaterialsAsync(source, commonMaterialPrewarmCancellation.Token);

            bool processedAnyCityObject = false;
            int processedCityObjectCount = 0;
            Stopwatch cityObjectStopwatch = Stopwatch.StartNew();

            await foreach (ResoniteConstructionCityObject cityObject in source.ReadCityObjectsAsync(cancellationToken))
            {
                processedAnyCityObject = true;
                await sceneBuilder.ProcessCityObjectAsync(cityObject, cancellationToken);
                processedCityObjectCount++;
            }

            await commonMaterialPrewarmTask;

            cityObjectStopwatch.Stop();
            ReportProgress(
                PlateauLog.Info("import", $"Streamed {processedCityObjectCount} city objects in {cityObjectStopwatch.Elapsed.TotalSeconds:F3}s."));

            if (!processedAnyCityObject)
            {
                throw new PlateauImportValidationException(
                    [$"No triangulated CityGML geometry was produced for mesh code '{resolvedRequest.MeshCode}'."]);
            }

            Stopwatch completeStopwatch = Stopwatch.StartNew();
            IReadOnlyList<string> destinations = await sceneBuilder.CompleteAsync(cancellationToken);
            completeStopwatch.Stop();
            ReportProgress(
                PlateauLog.Debug("import", $"Scene builder completion finished in {completeStopwatch.Elapsed.TotalSeconds:F3}s."));
            return new ImportExecutionResult(source.Metadata, destinations);
        }
        finally
        {
            if (commonMaterialPrewarmCancellation is not null)
            {
                await commonMaterialPrewarmCancellation.CancelAsync();
                if (!commonMaterialPrewarmTask.IsCompleted)
                {
                    _ = commonMaterialPrewarmTask.ContinueWith(
                        static completedTask => _ = completedTask.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
                else if (commonMaterialPrewarmTask.IsFaulted)
                {
                    _ = commonMaterialPrewarmTask.Exception;
                }

                commonMaterialPrewarmCancellation.Dispose();
            }

            await sceneBuilder.DisposeAsync();
        }
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }

    private async Task PrewarmCommonMaterialsAsync(
        IResoniteConstructionSource source,
        CancellationToken cancellationToken)
    {
        int preparedCommonMaterialCount = 0;
        await foreach (ResoniteMaterialBinding material in source.ReadCommonMaterialsAsync(cancellationToken))
        {
            await sceneBuilder.PrepareCommonMaterialAsync(material, cancellationToken);
            preparedCommonMaterialCount++;
        }

        if (preparedCommonMaterialCount > 0)
        {
            ReportProgress(
                PlateauLog.Debug("import", $"Prepared {preparedCommonMaterialCount} Common materials during source parsing."));
        }
    }

    private static PlateauImportRequest NormalizeRequestForValidation(PlateauImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request with
        {
            Dataset = TrimToEmpty(request.Dataset),
            MeshCode = TrimToEmpty(request.MeshCode),
            Source = NormalizeSourceForValidation(request.Source),
            PackageNames = request.PackageNames is null
                ? null
                : request.PackageNames.Select(static packageName => TrimToEmpty(packageName)).ToArray(),
        };
    }

    private static string TrimToEmpty(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static PlateauImportSource NormalizeSourceForValidation(PlateauImportSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source switch
        {
            PlateauLocalImportSource localSource => new PlateauLocalImportSource(
                string.IsNullOrWhiteSpace(localSource.LocalSourcePath)
                    ? null
                    : localSource.LocalSourcePath.Trim()),
            PlateauRemoteImportSource remoteSource => remoteSource.ServerUri is null
                ? new PlateauRemoteImportSource(null)
                : remoteSource,
            _ => source,
        };
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
