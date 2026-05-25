using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class NonDemCityObjectBaker(
    ResoniteTextureImageLoader textureImageLoader,
    IReadOnlyList<NonDemCityObjectBakePolicy> bakePolicies,
    int maxAtlasSize = 4096,
    int tilePaddingPixels = 2,
    ResoniteImportBudgetProfile? resourceBudget = null) : IResoniteBufferedCityObjectBaker
{
    internal const int DefaultMaxAtlasSize = 4096;
    internal const int DefaultTilePaddingPixels = 2;
    private readonly Dictionary<NonDemSourceFileBatchKey, List<NonDemBufferedCityObject>> bufferedCityObjectsBySourceFile = [];
    private readonly Dictionary<NonDemSourceFileBatchKey, int> nextBatchIndexBySourceFile = [];
    private readonly IReadOnlyList<NonDemCityObjectBakePolicy> bakePolicies = bakePolicies
        ?? throw new ArgumentNullException(nameof(bakePolicies));

    public string Name => "AtlasBake";

    public int BakedInputCityObjectCount { get; private set; }

    public int BakedOutputCityObjectCount { get; private set; }

    private int EffectiveMaxAtlasSize => Math.Max(1, Math.Min(maxAtlasSize, resourceBudget?.MaxAtlasSize ?? maxAtlasSize));

    private int EffectiveMaxAtlasTextureEdge
    {
        get
        {
            int profileMaxTileEdge = resourceBudget?.MaxAtlasTextureEdge ?? EffectiveMaxAtlasSize;
            return Math.Max(1, Math.Min(EffectiveMaxAtlasSize - (tilePaddingPixels * 2), profileMaxTileEdge));
        }
    }

    public ValueTask<BufferedCityObjectBufferResult> TryBufferAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        cancellationToken.ThrowIfCancellationRequested();

        NonDemCityObjectBakePolicy? policy = ResolvePolicy(cityObject);
        if (policy is null)
        {
            return ValueTask.FromResult(new BufferedCityObjectBufferResult(Buffered: false, []));
        }

        cityObject = ResoniteDynamicMaterialUvNormalizer.Normalize(cityObject);
        NonDemSourceFileBatchKey sourceFileKey = NonDemSourceFileBatching.CreateKey(cityObject, policy);
        List<ResoniteConstructionCityObject> readyCityObjects = [];
        NonDemBufferedCityObject bufferedCityObject = new(cityObject, policy);
        if (!bufferedCityObjectsBySourceFile.TryGetValue(sourceFileKey, out List<NonDemBufferedCityObject>? bufferedCityObjects))
        {
            bufferedCityObjects = [];
            bufferedCityObjectsBySourceFile.Add(sourceFileKey, bufferedCityObjects);
        }

        bufferedCityObjects.Add(bufferedCityObject);
        BakedInputCityObjectCount++;
        return ValueTask.FromResult(new BufferedCityObjectBufferResult(Buffered: true, readyCityObjects));
    }

    public async Task<IReadOnlyList<ResoniteConstructionCityObject>> FlushAllAsync(
        CancellationToken cancellationToken = default)
    {
        List<ResoniteConstructionCityObject> bakedCityObjects = [];
        await FlushAllAsync(
            (bakedCityObject, _) =>
            {
                bakedCityObjects.Add(bakedCityObject);
                return Task.CompletedTask;
            },
            cancellationToken);
        return bakedCityObjects;
    }

    public async Task FlushAllAsync(
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onBakedCityObject);
        if (bufferedCityObjectsBySourceFile.Count == 0)
        {
            return;
        }

        NonDemSourceFileBatchKey[] orderedSourceFileKeys = bufferedCityObjectsBySourceFile.Keys
            .OrderBy(static key => key, NonDemSourceFileBatching.KeyComparer)
            .ToArray();
        foreach (NonDemSourceFileBatchKey sourceFileKey in orderedSourceFileKeys)
        {
            await EmitSourceFileAsync(sourceFileKey, onBakedCityObject, cancellationToken);
        }
    }

    private async Task EmitSourceFileAsync(
        NonDemSourceFileBatchKey sourceFileKey,
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken)
    {
        if (!bufferedCityObjectsBySourceFile.Remove(sourceFileKey, out List<NonDemBufferedCityObject>? cityObjects))
        {
            return;
        }

        int emittedCount = 0;
        int batchStartIndex = nextBatchIndexBySourceFile.GetValueOrDefault(sourceFileKey);

        await BakeSourceFileAsync(
            sourceFileKey,
            cityObjects,
            batchStartIndex,
            (bakedCityObject, callbackCancellationToken) =>
            {
                emittedCount++;
                return onBakedCityObject(bakedCityObject, callbackCancellationToken);
            },
            cancellationToken);

        nextBatchIndexBySourceFile[sourceFileKey] = batchStartIndex + emittedCount;
        cityObjects.Clear();
    }

    private async Task BakeSourceFileAsync(
        NonDemSourceFileBatchKey sourceFileKey,
        List<NonDemBufferedCityObject> cityObjects,
        int batchStartIndex,
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken)
    {
        List<NonDemCityObjectBakeCandidate> passThroughCandidates = [];
        List<NonDemCityObjectBakeCandidate> currentAtlasBatch = [];
        int batchIndex = batchStartIndex;
        bool preservePrimaryIdentity = cityObjects.Count == 1;
        NonDemCityObjectBakeCandidateFactory candidateFactory = CreateCandidateFactory();

        foreach (NonDemBufferedCityObject bufferedCityObject in cityObjects.OrderBy(
                     static bufferedCityObject => bufferedCityObject.CityObject.SlotKey,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            NonDemCityObjectBakeCandidate? candidate = await candidateFactory.CreateAsync(bufferedCityObject, cancellationToken);
            if (candidate is null)
            {
                continue;
            }

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
                    await EmitFallbackCandidateAsync(candidate, onBakedCityObject, cancellationToken);
                }

                continue;
            }

            if (CanAppendToAtlasBatch(currentAtlasBatch, candidate))
            {
                currentAtlasBatch.Add(candidate);
                continue;
            }

            await EmitAtlasBatchAsync(
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
                await EmitFallbackCandidateAsync(candidate, onBakedCityObject, cancellationToken);
            }
        }

        if (currentAtlasBatch.Count > 0)
        {
            await EmitAtlasBatchAsync(
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
            BakedOutputCityObjectCount++;
            await onBakedCityObject(passThroughCandidate.CityObject, cancellationToken);
            DisposeCandidateImages(passThroughCandidate);
        }
        else if (passThroughCandidates.Count > 1)
        {
            try
            {
                ResoniteConstructionCityObject mergedPassThroughCityObject = await BakeBatchAsync(
                    sourceFileKey,
                    passThroughCandidates,
                    batchIndex,
                    preservePrimaryIdentity: false,
                    cancellationToken);
                BakedOutputCityObjectCount++;
                await onBakedCityObject(mergedPassThroughCityObject, cancellationToken);
            }
            finally
            {
                DisposeCandidateImages(passThroughCandidates);
            }
        }
    }

    private NonDemCityObjectBakePolicy? ResolvePolicy(ResoniteConstructionCityObject cityObject)
    {
        foreach (NonDemCityObjectBakePolicy policy in bakePolicies)
        {
            if (policy.CanBuffer(cityObject)
                && NonDemCityObjectBakeMaterialClassifier.CanBufferCityObjectMaterials(cityObject, policy))
            {
                return policy;
            }
        }

        return null;
    }

    private async Task<ResoniteConstructionCityObject> BakeBatchAsync(
        NonDemSourceFileBatchKey sourceFileKey,
        IReadOnlyList<NonDemCityObjectBakeCandidate> candidates,
        int batchIndex,
        bool preservePrimaryIdentity,
        CancellationToken cancellationToken)
    {
        NonDemCityObjectBakeAssembler assembler = new(CreateAtlasLayoutFactory(), new NonDemAtlasImageRenderer(tilePaddingPixels));
        return await assembler.BakeBatchAsync(
            sourceFileKey,
            candidates,
            batchIndex,
            preservePrimaryIdentity,
            cancellationToken);
    }

    private NonDemAtlasLayoutFactory CreateAtlasLayoutFactory()
    {
        return new NonDemAtlasLayoutFactory(EffectiveMaxAtlasSize, tilePaddingPixels);
    }

    private NonDemCityObjectBakeCandidateFactory CreateCandidateFactory()
    {
        return new NonDemCityObjectBakeCandidateFactory(textureImageLoader, EffectiveMaxAtlasTextureEdge);
    }

    private async Task EmitAtlasBatchAsync(
        NonDemSourceFileBatchKey sourceFileKey,
        IReadOnlyList<NonDemCityObjectBakeCandidate> batchCandidates,
        int batchIndex,
        bool preservePrimaryIdentity,
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken)
    {
        try
        {
            ResoniteConstructionCityObject bakedCityObject = await BakeBatchAsync(
                sourceFileKey,
                batchCandidates,
                batchIndex,
                preservePrimaryIdentity,
                cancellationToken);
            BakedOutputCityObjectCount++;
            await onBakedCityObject(bakedCityObject, cancellationToken);
        }
        finally
        {
            DisposeCandidateImages(batchCandidates);
        }
    }

    private async Task EmitFallbackCandidateAsync(
        NonDemCityObjectBakeCandidate fallbackCandidate,
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken)
    {
        try
        {
            BakedOutputCityObjectCount++;
            await onBakedCityObject(fallbackCandidate.CityObject, cancellationToken);
        }
        finally
        {
            DisposeCandidateImages(fallbackCandidate);
        }
    }

    private bool CanFitSingleCandidate(NonDemCityObjectBakeCandidate candidate)
    {
        return candidate.AtlasEntries.Count == 0 || CreateAtlasLayoutFactory().CanFit(candidate.AtlasEntries);
    }

    private bool CanAppendToAtlasBatch(
        IReadOnlyList<NonDemCityObjectBakeCandidate> batchCandidates,
        NonDemCityObjectBakeCandidate candidate)
    {
        List<NonDemAtlasBatchEntry> candidateEntries = [.. batchCandidates.SelectMany(static current => current.AtlasEntries), .. candidate.AtlasEntries];
        return CreateAtlasLayoutFactory().CanFit(candidateEntries);
    }

    private static void DisposeCandidateImages(NonDemCityObjectBakeCandidate candidate)
    {
        DisposeCandidateImages([candidate]);
    }

    private static void DisposeCandidateImages(IReadOnlyList<NonDemCityObjectBakeCandidate> candidates)
    {
        foreach (Image<Rgba32> tileImage in candidates
                     .SelectMany(static candidate => candidate.AtlasEntries)
                     .Select(static entry => entry.Tile.Image)
                     .Distinct())
        {
            tileImage.Dispose();
        }
    }

    private static bool RequiresBakeEmission(NonDemCityObjectBakeCandidate candidate)
    {
        return candidate.PreservedEntries.Any(static entry => entry.VertexColorOverride is not null);
    }

}
