namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendWorkerLaunchRequest(
    LiveSendRunState State,
    LiveSendQueuePlan QueuePlan,
    ResoniteImportBudgetProfile ResourceBudget,
    int ConnectionCount);
