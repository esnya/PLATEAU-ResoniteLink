namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteObjectSlotHierarchy(
    CreatedSlot AssetLodSlot,
    CreatedSlot LodSlot,
    string CityObjectSlotName,
    ResoniteFloat3 CityObjectLocalPosition,
    ResoniteFloatQ? CityObjectRotation);
