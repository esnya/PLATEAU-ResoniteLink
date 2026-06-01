using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal readonly record struct ObservedSourceRootSlot(
    string SlotName,
    string SlotId,
    string MeshCode,
    ResoniteFloat3 Position)
{
    public static bool TryCreate(Slot slot, out ObservedSourceRootSlot sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(slot.ID)
            || string.IsNullOrWhiteSpace(slot.Name?.Value)
            || !ResoniteSourceMeshCodeAnchor.TryGetConcreteMeshCode(slot.Name.Value, out string meshCode)
            || slot.Position is not Field_float3 position)
        {
            sourceRoot = default;
            return false;
        }

        sourceRoot = new ObservedSourceRootSlot(
            slot.Name.Value,
            slot.ID,
            meshCode,
            new ResoniteFloat3(position.Value.x, position.Value.y, position.Value.z));
        return true;
    }
}
