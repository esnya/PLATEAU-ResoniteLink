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

    public bool TryTake(
        NonDemSourceFileBatchKey sourceFileKey,
        out NonDemSourceFileBakeBufferEntry entry)
    {
        if (!bufferedCityObjectsBySourceFile.Remove(sourceFileKey, out List<NonDemBufferedCityObject>? cityObjects))
        {
            entry = default;
            return false;
        }

        entry = new NonDemSourceFileBakeBufferEntry(
            sourceFileKey,
            cityObjects,
            nextBatchIndexBySourceFile.GetValueOrDefault(sourceFileKey));
        return true;
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
