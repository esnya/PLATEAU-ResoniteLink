using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class CommonMaterialAssetCache
{
    public ConcurrentDictionary<string, Task> CommonMaterialFamilyWarmupTasks { get; } = new(StringComparer.Ordinal);

    public ResoniteCommonMaterialAssetAccumulator CommonMaterialAssets { get; } = new();

    public AsyncInFlightResultCache<BundledDefaultTextureAsset, Uri> BundledTextureImportTasks { get; } = new();
}
