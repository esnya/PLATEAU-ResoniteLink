using System.Collections.Generic;
using System.Linq;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface INonDemAtlasBatchFitPolicy
{
    bool CanFitSingleCandidate(NonDemCityObjectBakeCandidate candidate);

    bool CanAppendToAtlasBatch(
        IReadOnlyList<NonDemCityObjectBakeCandidate> batchCandidates,
        NonDemCityObjectBakeCandidate candidate);

    bool RequiresBakeEmission(NonDemCityObjectBakeCandidate candidate);
}

internal sealed class NonDemAtlasBatchFitPolicy(INonDemAtlasLayoutFactory atlasLayoutFactory) : INonDemAtlasBatchFitPolicy
{
    public bool CanFitSingleCandidate(NonDemCityObjectBakeCandidate candidate)
    {
        return candidate.AtlasEntries.Count == 0 || atlasLayoutFactory.CanFit(candidate.AtlasEntries);
    }

    public bool CanAppendToAtlasBatch(
        IReadOnlyList<NonDemCityObjectBakeCandidate> batchCandidates,
        NonDemCityObjectBakeCandidate candidate)
    {
        List<NonDemAtlasBatchEntry> candidateEntries = [.. batchCandidates.SelectMany(static current => current.AtlasEntries), .. candidate.AtlasEntries];
        return atlasLayoutFactory.CanFit(candidateEntries);
    }

    public bool RequiresBakeEmission(NonDemCityObjectBakeCandidate candidate)
    {
        return candidate.PreservedEntries.Any(static entry => entry.VertexColorOverride is not null);
    }
}
