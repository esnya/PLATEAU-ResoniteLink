using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlDocumentReadResult
{
    internal LocalCityGmlDocumentReadResult(
        LocalCityGmlDocumentSet documentSet,
        LocalCityGmlBootstrapContext bootstrapContext)
    {
        DocumentSet = documentSet;
        BootstrapContext = bootstrapContext;
    }

    public LocalCityGmlDocumentSet DocumentSet { get; }

    internal LocalCityGmlBootstrapContext BootstrapContext { get; }

    internal LocalCityGmlDocumentReadResult WithDocumentSet(LocalCityGmlDocumentSet documentSet)
    {
        return new LocalCityGmlDocumentReadResult(documentSet, BootstrapContext);
    }

    internal async Task<IReadOnlyList<DemTerrainOverlayRegion>> ResolveRequestedDemOverlayRegionsAsync(
        IReadOnlyList<string> requestedDemMeshCodes,
        CancellationToken cancellationToken)
    {
        SourceFilePipeline[] demPipelines = BootstrapContext.SourceFilePipelines
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
