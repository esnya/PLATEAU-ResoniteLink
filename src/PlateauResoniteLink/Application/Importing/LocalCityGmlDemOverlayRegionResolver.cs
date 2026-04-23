using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal static class LocalCityGmlDemOverlayRegionResolver
{
    internal static async Task<IReadOnlyList<DemTerrainOverlayRegion>> ResolveAsync(
        LocalCityGmlBootstrapContext bootstrapContext,
        IReadOnlyList<string> requestedDemMeshCodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bootstrapContext);
        ArgumentNullException.ThrowIfNull(requestedDemMeshCodes);

        SourceFilePipeline[] demPipelines = bootstrapContext.SourceFilePipelines
            .Where(static pipeline => string.Equals(
                pipeline.SourceFile.PackageName,
                "dem",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (demPipelines.Length == 0)
        {
            return LocalCityGmlDemBootstrapSupport.CreateDemTerrainOverlayRegions(requestedDemMeshCodes);
        }

        ParsedSourceFileResult[] parsedDemSourceFiles = await Task.WhenAll(
            demPipelines.Select(pipeline => pipeline.GetParseTask().WaitAsync(cancellationToken)));
        DemTerrainBounds? demBounds = LocalCityGmlDemBootstrapSupport.ResolveDemTerrainBounds(
            parsedDemSourceFiles,
            fallbackBounds: null);
        return demBounds is null
            ? LocalCityGmlDemBootstrapSupport.CreateDemTerrainOverlayRegions(requestedDemMeshCodes)
            : LocalCityGmlDemBootstrapSupport.CreateDemTerrainOverlayRegions(demBounds, requestedDemMeshCodes);
    }
}
