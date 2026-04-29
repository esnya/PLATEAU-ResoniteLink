using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemOverlayRegionResolver
{
    internal static async Task<IReadOnlyList<DemTerrainOverlayRegion>> ResolveAsync(
        ImportedSceneSourceContext discoveryContext,
        IReadOnlyList<string> requestedDemMeshCodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(discoveryContext);
        ArgumentNullException.ThrowIfNull(requestedDemMeshCodes);

        SourceFilePipeline[] demPipelines = discoveryContext.SourceFilePipelines
            .Where(static pipeline => string.Equals(
                pipeline.SourceFile.PackageName,
                "dem",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (demPipelines.Length == 0)
        {
            return DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(requestedDemMeshCodes);
        }

        ParsedSourceFileResult[] parsedDemSourceFiles = await Task.WhenAll(
            demPipelines.Select(pipeline => pipeline.GetParseTask().WaitAsync(cancellationToken)));
        DemTerrainBounds? demBounds = DemSourceDiscoverySupport.ResolveDemTerrainBounds(
            parsedDemSourceFiles,
            fallbackBounds: null);
        return demBounds is null
            ? DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(requestedDemMeshCodes)
            : DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(demBounds, requestedDemMeshCodes);
    }
}
