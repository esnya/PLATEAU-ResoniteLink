using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class NonDemSourceFileBakeEmitter(
    NonDemCityObjectBakeCandidateFactory candidateFactory,
    NonDemCityObjectBakeAssembler assembler,
    NonDemAtlasLayoutFactory atlasLayoutFactory)
{
    private readonly NonDemCityObjectBakeCandidateFactory candidateFactory = candidateFactory
        ?? throw new ArgumentNullException(nameof(candidateFactory));
    private readonly NonDemCityObjectBakeAssembler assembler = assembler
        ?? throw new ArgumentNullException(nameof(assembler));
    private readonly NonDemAtlasLayoutFactory atlasLayoutFactory = atlasLayoutFactory
        ?? throw new ArgumentNullException(nameof(atlasLayoutFactory));

    public async Task<int> EmitAsync(
        NonDemSourceFileBatchKey sourceFileKey,
        IReadOnlyList<NonDemBufferedCityObject> cityObjects,
        int batchStartIndex,
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken)
    {
        List<NonDemCityObjectBakeCandidate> passThroughCandidates = [];
        List<NonDemCityObjectBakeCandidate> currentAtlasBatch = [];
        int emittedCount = 0;
        int batchIndex = batchStartIndex;
        bool preservePrimaryIdentity = cityObjects.Count == 1;

        foreach (NonDemBufferedCityObject bufferedCityObject in cityObjects.OrderBy(
                     static bufferedCityObject => bufferedCityObject.CityObject.SlotKey,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            NonDemCityObjectBakeCandidate candidate = await candidateFactory.CreateAsync(bufferedCityObject, cancellationToken);

            if (candidate.AtlasEntries.Count == 0 && !RequiresBakeEmission(candidate))
            {
                passThroughCandidates.Add(candidate);
                continue;
            }

            if (currentAtlasBatch.Count == 0)
            {
                if (CanFitSingleCandidate(candidate))
                {
                    currentAtlasBatch.Add(candidate);
                }
                else
                {
                    emittedCount += await EmitFallbackCandidateAsync(candidate, onBakedCityObject, cancellationToken);
                }

                continue;
            }

            if (CanAppendToAtlasBatch(currentAtlasBatch, candidate))
            {
                currentAtlasBatch.Add(candidate);
                continue;
            }

            emittedCount += await EmitAtlasBatchAsync(
                sourceFileKey,
                currentAtlasBatch,
                batchIndex++,
                preservePrimaryIdentity && passThroughCandidates.Count == 0,
                onBakedCityObject,
                cancellationToken);
            currentAtlasBatch.Clear();

            if (CanFitSingleCandidate(candidate))
            {
                currentAtlasBatch.Add(candidate);
            }
            else
            {
                emittedCount += await EmitFallbackCandidateAsync(candidate, onBakedCityObject, cancellationToken);
            }
        }

        if (currentAtlasBatch.Count > 0)
        {
            emittedCount += await EmitAtlasBatchAsync(
                sourceFileKey,
                currentAtlasBatch,
                batchIndex++,
                preservePrimaryIdentity && passThroughCandidates.Count == 0,
                onBakedCityObject,
                cancellationToken);
            currentAtlasBatch.Clear();
        }

        if (passThroughCandidates.Count == 1)
        {
            NonDemCityObjectBakeCandidate passThroughCandidate = passThroughCandidates[0];
            await onBakedCityObject(passThroughCandidate.CityObject, cancellationToken);
            NonDemBakeCandidateImageDisposer.Dispose(passThroughCandidate);
            emittedCount++;
        }
        else if (passThroughCandidates.Count > 1)
        {
            try
            {
                ResoniteConstructionCityObject mergedPassThroughCityObject = await assembler.BakeBatchAsync(
                    sourceFileKey,
                    passThroughCandidates,
                    batchIndex,
                    preservePrimaryIdentity: false,
                    cancellationToken);
                await onBakedCityObject(mergedPassThroughCityObject, cancellationToken);
                emittedCount++;
            }
            finally
            {
                NonDemBakeCandidateImageDisposer.Dispose(passThroughCandidates);
            }
        }

        return emittedCount;
    }

    private bool CanFitSingleCandidate(NonDemCityObjectBakeCandidate candidate)
    {
        return candidate.AtlasEntries.Count == 0 || atlasLayoutFactory.CanFit(candidate.AtlasEntries);
    }

    private bool CanAppendToAtlasBatch(
        IReadOnlyList<NonDemCityObjectBakeCandidate> batchCandidates,
        NonDemCityObjectBakeCandidate candidate)
    {
        List<NonDemAtlasBatchEntry> candidateEntries = [.. batchCandidates.SelectMany(static current => current.AtlasEntries), .. candidate.AtlasEntries];
        return atlasLayoutFactory.CanFit(candidateEntries);
    }

    private static bool RequiresBakeEmission(NonDemCityObjectBakeCandidate candidate)
    {
        return candidate.PreservedEntries.Any(static entry => entry.VertexColorOverride is not null);
    }

    private async Task<int> EmitAtlasBatchAsync(
        NonDemSourceFileBatchKey sourceFileKey,
        IReadOnlyList<NonDemCityObjectBakeCandidate> batchCandidates,
        int batchIndex,
        bool preservePrimaryIdentity,
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken)
    {
        try
        {
            ResoniteConstructionCityObject bakedCityObject = await assembler.BakeBatchAsync(
                sourceFileKey,
                batchCandidates,
                batchIndex,
                preservePrimaryIdentity,
                cancellationToken);
            await onBakedCityObject(bakedCityObject, cancellationToken);
            return 1;
        }
        finally
        {
            NonDemBakeCandidateImageDisposer.Dispose(batchCandidates);
        }
    }

    private static async Task<int> EmitFallbackCandidateAsync(
        NonDemCityObjectBakeCandidate fallbackCandidate,
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken)
    {
        try
        {
            await onBakedCityObject(fallbackCandidate.CityObject, cancellationToken);
            return 1;
        }
        finally
        {
            NonDemBakeCandidateImageDisposer.Dispose(fallbackCandidate);
        }
    }
}
