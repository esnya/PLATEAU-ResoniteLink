using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class NonDemCityObjectBaker(
    INonDemCityObjectBakePolicyResolver bakePolicyResolver,
    INonDemSourceFileBakeEmitter sourceFileBakeEmitter) : IResoniteBufferedCityObjectBaker
{
    private readonly Dictionary<NonDemSourceFileBatchKey, List<NonDemBufferedCityObject>> bufferedCityObjectsBySourceFile = [];
    private readonly Dictionary<NonDemSourceFileBatchKey, int> nextBatchIndexBySourceFile = [];
    private readonly INonDemCityObjectBakePolicyResolver bakePolicyResolver = bakePolicyResolver
        ?? throw new ArgumentNullException(nameof(bakePolicyResolver));
    private readonly INonDemSourceFileBakeEmitter sourceFileBakeEmitter = sourceFileBakeEmitter
        ?? throw new ArgumentNullException(nameof(sourceFileBakeEmitter));

    public string Name => "AtlasBake";

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

        int batchStartIndex = nextBatchIndexBySourceFile.GetValueOrDefault(sourceFileKey);
        int emittedCount = await sourceFileBakeEmitter.EmitAsync(
            sourceFileKey,
            cityObjects,
            batchStartIndex,
            onBakedCityObject,
            cancellationToken);

        BakedOutputCityObjectCount += emittedCount;
        nextBatchIndexBySourceFile[sourceFileKey] = batchStartIndex + emittedCount;
        cityObjects.Clear();
    }
}
