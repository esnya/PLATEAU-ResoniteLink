namespace PlateauResoniteLink.Targets.Resonite;

internal readonly record struct ResoniteSlotLocator(string Value)
{
    public static ResoniteSlotLocator Root { get; } = new("Root");

    public override string ToString() => Value;
}

internal readonly record struct ResoniteComponentLocator(string Value)
{
    public override string ToString() => Value;
}

internal readonly record struct CreatedSlot(
    ResoniteSlotLocator Locator,
    string SlotName);

internal readonly record struct CreatedComponent(
    ResoniteComponentLocator Locator,
    string ComponentType);

internal readonly record struct CreatedMaterialAsset(
    ResoniteComponentLocator MaterialComponent,
    ResoniteComponentLocator? MaterialPropertyBlockComponent);
