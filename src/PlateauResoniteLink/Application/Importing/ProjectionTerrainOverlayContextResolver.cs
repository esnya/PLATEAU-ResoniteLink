using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record ProjectionTerrainOverlayContext(IReadOnlyList<TerrainTextureOverlay> Overlays)
{
    public static ProjectionTerrainOverlayContext Empty { get; } = new([]);
}

internal sealed class ProjectionTerrainOverlayContextResolver
{
    private readonly PlateauImportRequest request;
    private readonly SourceFilePipeline[] sourceFiles;
    private readonly MeshCodeBounds[] requestedMeshCodeBounds;
    private readonly string[] selectedMeshCodes;
    private readonly TerrainTextureOverlay[] discoveryTerrainTextureOverlays;
    private readonly bool hasDemPackage;
    private readonly ResolveDemTextureSources resolveDemTextureSources;
    private readonly object parsedDemSourceFilesGate = new();
    private readonly object demOverlayRegionsGate = new();
    private readonly object demTextureSourcesGate = new();
    private readonly object overlayContextGate = new();
    private Task<ParsedSourceFileResult[]>? parsedDemSourceFilesTask;
    private Task<DemTerrainOverlayRegion[]>? demOverlayRegionsTask;
    private Task<ResolvedDemTextureSources>? demTextureSourcesTask;
    private Task<ProjectionTerrainOverlayContext>? overlayContextTask;

    internal ProjectionTerrainOverlayContextResolver(
        PlateauImportRequest request,
        IReadOnlyList<SourceFilePipeline> sourceFiles,
        IReadOnlyList<TerrainTextureOverlay> discoveryTerrainTextureOverlays,
        IReadOnlyList<string> selectedMeshCodes,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        bool hasDemPackage,
        ResolveDemTextureSources resolveDemTextureSources)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentNullException.ThrowIfNull(discoveryTerrainTextureOverlays);
        ArgumentNullException.ThrowIfNull(selectedMeshCodes);
        ArgumentNullException.ThrowIfNull(requestedMeshCodeBounds);
        ArgumentNullException.ThrowIfNull(resolveDemTextureSources);

        this.request = request;
        this.sourceFiles = sourceFiles.ToArray();
        this.discoveryTerrainTextureOverlays = discoveryTerrainTextureOverlays.ToArray();
        this.selectedMeshCodes = selectedMeshCodes.ToArray();
        this.requestedMeshCodeBounds = requestedMeshCodeBounds.ToArray();
        this.hasDemPackage = hasDemPackage;
        this.resolveDemTextureSources = resolveDemTextureSources;
    }

    public async Task ValidateBeforeSinkSetupAsync(CancellationToken cancellationToken = default)
    {
        if (request.DemTextureSource is null || !hasDemPackage)
        {
            return;
        }

        _ = await GetDemTextureSourcesAsync(useParsedDemCoverage: true, cancellationToken);
    }

    public async Task<ProjectionTerrainOverlayContext> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!hasDemPackage)
        {
            return ProjectionTerrainOverlayContext.Empty;
        }

        Task<ProjectionTerrainOverlayContext> contextTask;
        lock (overlayContextGate)
        {
            contextTask = overlayContextTask ??= CreateAsync(CancellationToken.None);
        }

        try
        {
            return await contextTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (contextTask.IsCanceled || contextTask.IsFaulted)
            {
                lock (overlayContextGate)
                {
                    if (ReferenceEquals(overlayContextTask, contextTask))
                    {
                        overlayContextTask = null;
                    }
                }
            }

            throw;
        }
    }

    private async Task<ProjectionTerrainOverlayContext> CreateAsync(CancellationToken cancellationToken)
    {
        if (request.DemTextureSource is null
            && !request.ExcludeGsiTerrainTiles
            && discoveryTerrainTextureOverlays.Length > 0
            && await HasSceneDemOverlayCoverageAsync(discoveryTerrainTextureOverlays, cancellationToken))
        {
            return new ProjectionTerrainOverlayContext(discoveryTerrainTextureOverlays);
        }

        bool useParsedDemCoverage = request.DemTextureSource is not null || discoveryTerrainTextureOverlays.Length > 0;
        ResolvedDemTextureSources resolvedDemTextureSources = await GetDemTextureSourcesAsync(
            useParsedDemCoverage,
            cancellationToken);
        return new ProjectionTerrainOverlayContext(resolvedDemTextureSources.Overlays);
    }

    private async Task<bool> HasSceneDemOverlayCoverageAsync(
        IReadOnlyList<TerrainTextureOverlay> overlays,
        CancellationToken cancellationToken)
    {
        ParsedSourceFileResult[] parsedDemSourceFiles = await GetParsedDemSourceFilesAsync(cancellationToken);
        foreach (ParsedSourceFileResult parsedSourceFile in parsedDemSourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (ParsedCityObject parsedCityObject in parsedSourceFile.CityObjects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DemTerrainOverlayAssignment.HasOverlayCoverage(
                        parsedCityObject,
                        overlays,
                        requestedMeshCodeBounds))
                {
                    continue;
                }

                return false;
            }
        }

        return true;
    }

    private async Task<ResolvedDemTextureSources> GetDemTextureSourcesAsync(
        bool useParsedDemCoverage,
        CancellationToken cancellationToken)
    {
        DemTerrainOverlayRegion[] overlayRegions = await GetDemOverlayRegionsAsync(
            useParsedDemCoverage,
            cancellationToken);
        if (overlayRegions.Length == 0)
        {
            return new ResolvedDemTextureSources([]);
        }

        Task<ResolvedDemTextureSources> textureSourcesTask;
        lock (demTextureSourcesGate)
        {
            textureSourcesTask = demTextureSourcesTask ??= resolveDemTextureSources(
                request,
                overlayRegions,
                CancellationToken.None);
        }

        try
        {
            return await textureSourcesTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (textureSourcesTask.IsCanceled || textureSourcesTask.IsFaulted)
            {
                lock (demTextureSourcesGate)
                {
                    if (ReferenceEquals(demTextureSourcesTask, textureSourcesTask))
                    {
                        demTextureSourcesTask = null;
                    }
                }
            }

            throw;
        }
    }

    private async Task<DemTerrainOverlayRegion[]> GetDemOverlayRegionsAsync(
        bool useParsedDemCoverage,
        CancellationToken cancellationToken)
    {
        if (!useParsedDemCoverage)
        {
            return CreateRequestedDemOverlayRegions();
        }

        Task<DemTerrainOverlayRegion[]> overlayRegionsTask;
        lock (demOverlayRegionsGate)
        {
            overlayRegionsTask = demOverlayRegionsTask ??= CreateSceneDemOverlayRegionsAsync(CancellationToken.None);
        }

        try
        {
            return await overlayRegionsTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (overlayRegionsTask.IsCanceled || overlayRegionsTask.IsFaulted)
            {
                lock (demOverlayRegionsGate)
                {
                    if (ReferenceEquals(demOverlayRegionsTask, overlayRegionsTask))
                    {
                        demOverlayRegionsTask = null;
                    }
                }
            }

            throw;
        }
    }

    private async Task<DemTerrainOverlayRegion[]> CreateSceneDemOverlayRegionsAsync(
        CancellationToken cancellationToken)
    {
        ParsedSourceFileResult[] parsedDemSourceFiles = await GetParsedDemSourceFilesAsync(cancellationToken);
        DemTerrainBounds? demBounds = DemSourceDiscoverySupport.ResolveDemTerrainBounds(
            parsedDemSourceFiles,
            ResolveRequestedDemTerrainBounds());
        return demBounds is null
            ? CreateRequestedDemOverlayRegions()
            : DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(demBounds, GetRequestedMeshCodes());
    }

    private DemTerrainOverlayRegion[] CreateRequestedDemOverlayRegions()
    {
        return DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(GetRequestedMeshCodes());
    }

    private string[] GetRequestedMeshCodes()
    {
        return selectedMeshCodes.Length == 0 ? [request.MeshCode] : selectedMeshCodes;
    }

    private async Task<ParsedSourceFileResult[]> GetParsedDemSourceFilesAsync(
        CancellationToken cancellationToken)
    {
        Task<ParsedSourceFileResult[]> sourceFilesTask;
        lock (parsedDemSourceFilesGate)
        {
            sourceFilesTask = parsedDemSourceFilesTask ??= Task.WhenAll(
                sourceFiles
                    .Where(static sourceFile => string.Equals(sourceFile.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
                    .Select(static sourceFile => sourceFile.GetParseTask()));
        }

        try
        {
            return await sourceFilesTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (sourceFilesTask.IsCanceled || sourceFilesTask.IsFaulted)
            {
                lock (parsedDemSourceFilesGate)
                {
                    if (ReferenceEquals(parsedDemSourceFilesTask, sourceFilesTask))
                    {
                        parsedDemSourceFilesTask = null;
                    }
                }
            }

            throw;
        }
    }

    private DemTerrainBounds? ResolveRequestedDemTerrainBounds()
    {
        return MeshCodeBounds.TryMerge(requestedMeshCodeBounds) is { } requestedMeshBounds
            ? new DemTerrainBounds(
                requestedMeshBounds.SouthLatitude,
                requestedMeshBounds.NorthLatitude,
                requestedMeshBounds.WestLongitude,
                requestedMeshBounds.EastLongitude)
            : null;
    }
}
