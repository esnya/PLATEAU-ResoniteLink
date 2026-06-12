using System;
using System.IO;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class LiveSendRunPlanFactory
{
    private const int MaxQueuedCityObjects = 4;
    private const long MaxInFlightCityObjectWorkingSetBytesPerLane = 256L * 1024L * 1024L;
    private const long MaxInFlightCityObjectWorkingSetBytesFloor = 512L * 1024L * 1024L;

    public static LiveSendRunPlan Create(
        ResoniteSceneSetupInfo setupInfo,
        string workRoot,
        ResoniteLocalOrigin requestLocalOrigin,
        ResoniteImportMemoryProfile memoryProfile,
        int connectionCount,
        bool meshBakeEnabled)
    {
        ArgumentNullException.ThrowIfNull(setupInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);
        ArgumentOutOfRangeException.ThrowIfLessThan(connectionCount, 1);

        ResoniteImportBudgetProfile resourceBudget = ResoniteImportBudgetProfiles.ForProfile(memoryProfile);
        return new LiveSendRunPlan(
            setupInfo,
            Path.GetFullPath(workRoot),
            requestLocalOrigin,
            ResonitePlacementPolicy.CreateSourceFileSlotNamesByRelativePath(
                setupInfo.SourceFiles,
                setupInfo.SourceFilePackageNamesByRelativePath),
            resourceBudget,
            new LiveSendQueuePlan(
                connectionCount,
                Math.Max(MaxQueuedCityObjects * connectionCount, connectionCount),
                Math.Max(
                    resourceBudget.ImportWorkingSetBytes,
                    Math.Max(
                        MaxInFlightCityObjectWorkingSetBytesFloor,
                        connectionCount * MaxInFlightCityObjectWorkingSetBytesPerLane))),
            meshBakeEnabled);
    }
}
