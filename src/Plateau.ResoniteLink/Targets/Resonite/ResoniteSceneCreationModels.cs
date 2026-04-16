namespace Plateau.ResoniteLink.Cli;

internal readonly record struct CreatedSlot(
    string SlotId,
    string SlotName);

internal readonly record struct CreatedComponent(
    string ComponentId,
    string ComponentType);
