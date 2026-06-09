using System.Collections.Generic;
using System.Linq;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class NonDemSourceFileBakeBuffer
{
    private readonly Dictionary<NonDemSourceFileBatchKey, List<NonDemBufferedCityObject>> bufferedCityObjectsBySourceFile = [];
    private readonly Dictionary<NonDemSourceFileBatchKey, int> nextBatchIndexBySourceFile = [];

    public bool IsEmpty => bufferedCityObjectsBySourceFile.Count == 0;

    public void Add(
        NonDemSourceFileBatchKey sourceFileKey,
        NonDemBufferedCityObject bufferedCityObject)
    {
        if (!bufferedCityObjectsBySourceFile.TryGetValue(sourceFileKey, out List<NonDemBufferedCityObject>? bufferedCityObjects))
        {
            bufferedCityObjects = [];
            bufferedCityObjectsBySourceFile.Add(sourceFileKey, bufferedCityObjects);
        }

        bufferedCityObjects.Add(bufferedCityObject);
    }

    public IReadOnlyList<NonDemSourceFileBatchKey> SnapshotOrderedSourceFileKeys()
    {
        return bufferedCityObjectsBySourceFile.Keys
            .OrderBy(static key => key, NonDemSourceFileBatching.KeyComparer)
            .ToArray();
    }

    public NonDemSourceFileBakeBufferTakeResult Take(NonDemSourceFileBatchKey sourceFileKey)
    {
        if (!bufferedCityObjectsBySourceFile.Remove(sourceFileKey, out List<NonDemBufferedCityObject>? cityObjects))
        {
            return NonDemSourceFileBakeBufferTakeResult.NotFound;
        }

        return new NonDemSourceFileBakeBufferTakeResult(
            Found: true,
            new NonDemSourceFileBakeBufferEntry(
                sourceFileKey,
                cityObjects,
                nextBatchIndexBySourceFile.GetValueOrDefault(sourceFileKey)));
    }

    public void Complete(
        NonDemSourceFileBakeBufferEntry entry,
        int reservedOutputCount)
    {
        nextBatchIndexBySourceFile[entry.SourceFileKey] = entry.BatchStartIndex + reservedOutputCount;
        entry.CityObjects.Clear();
    }
}

internal readonly record struct NonDemSourceFileBakeBufferEntry(
    NonDemSourceFileBatchKey SourceFileKey,
    List<NonDemBufferedCityObject> CityObjects,
    int BatchStartIndex);

internal readonly record struct NonDemSourceFileBakeBufferTakeResult(
    bool Found,
    NonDemSourceFileBakeBufferEntry Entry)
{
    public static NonDemSourceFileBakeBufferTakeResult NotFound => new(Found: false, default);
}
