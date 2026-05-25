namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendRunContext(
    LiveSendRunPlan Plan,
    CreatedSlot DatasetRootSlot,
    CreatedSlot DatasetAssetsRootSlot,
    CreatedSlot CommonAssetsRootSlot,
    CompositeCityObjectBaker? CityObjectBaker);
