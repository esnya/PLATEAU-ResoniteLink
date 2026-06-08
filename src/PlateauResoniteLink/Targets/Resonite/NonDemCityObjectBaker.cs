using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class NonDemCityObjectBaker(
    NonDemCityObjectBakePolicyResolver bakePolicyResolver,
    NonDemSourceFileBakeEmitter sourceFileBakeEmitter)
{
    private readonly NonDemSourceFileBakeBuffer sourceFileBuffer = new();
    private readonly NonDemCityObjectBakePolicyResolver bakePolicyResolver = bakePolicyResolver
        ?? throw new ArgumentNullException(nameof(bakePolicyResolver));
    private readonly NonDemSourceFileBakeEmitter sourceFileBakeEmitter = sourceFileBakeEmitter
        ?? throw new ArgumentNullException(nameof(sourceFileBakeEmitter));

    public static string Name => "AtlasBake";

    public int BakedInputCityObjectCount { get; private set; }

    public int BakedOutputCityObjectCount { get; private set; }

    public ValueTask<BufferedCityObjectBufferResult> TryBufferAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        cancellationToken.ThrowIfCancellationRequested();

        NonDemCityObjectBakePolicy? policy = bakePolicyResolver.Resolve(cityObject);
        if (policy is null)
        {
            return ValueTask.FromResult(new BufferedCityObjectBufferResult(Buffered: false, []));
        }

        ResoniteConstructionCityObject normalizedCityObject = ResoniteDynamicMaterialUvNormalizer.Normalize(cityObject);
        NonDemSourceFileBatchKey sourceFileKey = NonDemSourceFileBatching.CreateKey(normalizedCityObject, policy);
        List<ResoniteConstructionCityObject> readyCityObjects = [];
        sourceFileBuffer.Add(sourceFileKey, new NonDemBufferedCityObject(normalizedCityObject, policy));
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
        if (sourceFileBuffer.IsEmpty)
        {
            return;
        }

        foreach (NonDemSourceFileBatchKey sourceFileKey in sourceFileBuffer.SnapshotOrderedSourceFileKeys())
        {
            await EmitSourceFileAsync(sourceFileKey, onBakedCityObject, cancellationToken);
        }
    }

    private async Task EmitSourceFileAsync(
        NonDemSourceFileBatchKey sourceFileKey,
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken)
    {
        if (!sourceFileBuffer.TryTake(sourceFileKey, out NonDemSourceFileBakeBufferEntry bufferEntry))
        {
            return;
        }

        int emittedCount = 0;
        try
        {
            await sourceFileBakeEmitter.EmitAsync(
                bufferEntry.SourceFileKey,
                bufferEntry.CityObjects,
                bufferEntry.BatchStartIndex,
                async (bakedCityObject, callbackCancellationToken) =>
                {
                    emittedCount++;
                    await onBakedCityObject(bakedCityObject, callbackCancellationToken);
                    BakedOutputCityObjectCount++;
                },
                cancellationToken);
        }
        finally
        {
            sourceFileBuffer.Complete(bufferEntry, emittedCount);
        }
    }
}
