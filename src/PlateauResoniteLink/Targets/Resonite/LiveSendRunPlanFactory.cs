using System;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface ILiveSendRunPlanFactory
{
    LiveSendRunPlan Create(
        ResoniteSceneSetupInfo setupInfo,
        string resolvedWorkRoot,
        ResoniteLocalOrigin requestLocalOrigin,
        ResoniteImportMemoryProfile memoryProfile,
        int connectionCount,
        bool meshBakeEnabled);
}

internal sealed class LiveSendRunPlanFactory : ILiveSendRunPlanFactory
{
    private const int MaxQueuedCityObjects = 4;
    private const long MaxInFlightCityObjectWorkingSetBytesPerLane = 256L * 1024L * 1024L;
    private const long MaxInFlightCityObjectWorkingSetBytesFloor = 512L * 1024L * 1024L;

    public LiveSendRunPlan Create(
        ResoniteSceneSetupInfo setupInfo,
        string resolvedWorkRoot,
        ResoniteLocalOrigin requestLocalOrigin,
        ResoniteImportMemoryProfile memoryProfile,
        int connectionCount,
        bool meshBakeEnabled)
    {
        ArgumentNullException.ThrowIfNull(setupInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedWorkRoot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(connectionCount);

        ResoniteImportBudgetProfile resourceBudget = ResoniteImportBudgetProfiles.ForProfile(memoryProfile);
        return new LiveSendRunPlan(
            setupInfo,
            resolvedWorkRoot,
            requestLocalOrigin,
            ResonitePlacementPolicy.CreateSourceFileSlotNamesByRelativePath(setupInfo.SourceFiles),
            resourceBudget,
            new LiveSendQueuePlan(
                connectionCount,
                Math.Max(MaxQueuedCityObjects * connectionCount, connectionCount),
                Math.Max(resourceBudget.ImportWorkingSetBytes,
                    Math.Max(
                        MaxInFlightCityObjectWorkingSetBytesFloor,
                        connectionCount * MaxInFlightCityObjectWorkingSetBytesPerLane))),
            meshBakeEnabled);
    }
}
