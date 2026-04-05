namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteConstructionBuilding(
    string SlotKey,
    string DisplayName,
    ResoniteTransform Transform,
    ResoniteImportedMesh Mesh,
    IReadOnlyList<ResoniteMaterialBinding> Materials);
