namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteConstructionCityObject(
    string SlotKey,
    string DisplayName,
    string PackageName,
    ResoniteTransform Transform,
    ResoniteImportedMesh Mesh,
    IReadOnlyList<ResoniteMaterialBinding> Materials);
