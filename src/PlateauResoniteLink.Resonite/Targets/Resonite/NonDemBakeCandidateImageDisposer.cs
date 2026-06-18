using System.Collections.Generic;
using System.Linq;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface INonDemBakeCandidateImageDisposer
{
    void Dispose(NonDemCityObjectBakeCandidate candidate);

    void Dispose(IReadOnlyList<NonDemCityObjectBakeCandidate> candidates);
}

internal sealed class NonDemBakeCandidateImageDisposer : INonDemBakeCandidateImageDisposer
{
    public void Dispose(NonDemCityObjectBakeCandidate candidate)
    {
        Dispose([candidate]);
    }

    public void Dispose(IReadOnlyList<NonDemCityObjectBakeCandidate> candidates)
    {
        foreach (Image<Rgba32> tileImage in candidates
                     .SelectMany(static candidate => candidate.AtlasEntries)
                     .Select(static entry => entry.Tile.Image)
                     .Distinct())
        {
            tileImage.Dispose();
        }
    }
}
