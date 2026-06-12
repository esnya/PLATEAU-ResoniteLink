namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteObjectSlotHierarchy(
    CreatedSlot LodSlot,
    string CityObjectSlotName,
    ResoniteFloat3 CityObjectLocalPosition,
    ResoniteFloatQ? CityObjectRotation,
    long? CityObjectOrderOffset = null,
    CreatedSlot? SourceFileSlot = null);
