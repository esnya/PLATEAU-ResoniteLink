using System;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal sealed record ResoniteImportBudgetProfile(
    ResoniteImportMemoryProfile Name,
    long ImportWorkingSetBytes,
    long RuntimeVramBudgetBytes,
    int MaxAtlasSize,
    int MaxAtlasTextureEdge);

internal static class ResoniteImportBudgetProfiles
{
    public static readonly ResoniteImportBudgetProfile Small = new(
        ResoniteImportMemoryProfile.Small,
        ImportWorkingSetBytes: 384L * 1024L * 1024L,
        RuntimeVramBudgetBytes: 1536L * 1024L * 1024L,
        MaxAtlasSize: 1024,
        MaxAtlasTextureEdge: 512);

    public static readonly ResoniteImportBudgetProfile Large = new(
        ResoniteImportMemoryProfile.Large,
        ImportWorkingSetBytes: 1024L * 1024L * 1024L,
        RuntimeVramBudgetBytes: 4096L * 1024L * 1024L,
        MaxAtlasSize: 4096,
        MaxAtlasTextureEdge: 4096);

    public static ResoniteImportBudgetProfile ForProfile(ResoniteImportMemoryProfile profile)
    {
        return profile switch
        {
            ResoniteImportMemoryProfile.Small => Small,
            ResoniteImportMemoryProfile.Large => Large,
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unsupported memory profile."),
        };
    }
}
