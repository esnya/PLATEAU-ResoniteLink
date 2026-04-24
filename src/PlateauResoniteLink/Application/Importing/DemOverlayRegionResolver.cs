using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemOverlayRegionResolver
{
    internal static async Task<IReadOnlyList<DemTerrainOverlayRegion>> ResolveAsync(
        ImportedSceneSourceContext bootstrapContext,
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
            return DemSourceBootstrapSupport.CreateDemTerrainOverlayRegions(requestedDemMeshCodes);
        }

        ParsedSourceFileResult[] parsedDemSourceFiles = await Task.WhenAll(
            demPipelines.Select(pipeline => pipeline.GetParseTask().WaitAsync(cancellationToken)));
        DemTerrainBounds? demBounds = DemSourceBootstrapSupport.ResolveDemTerrainBounds(
            parsedDemSourceFiles,
            fallbackBounds: null);
        return demBounds is null
            ? DemSourceBootstrapSupport.CreateDemTerrainOverlayRegions(requestedDemMeshCodes)
            : DemSourceBootstrapSupport.CreateDemTerrainOverlayRegions(demBounds, requestedDemMeshCodes);
    }
}
