namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteConstructionCityObject(
    string SlotKey,
    string DisplayName,
    string PackageName,
    int? LodLevel,
    ResoniteTransform Transform,
    ResoniteImportedMesh Mesh,
    IReadOnlyList<ResoniteMaterialBinding> Materials,
    bool CollisionEnabled = true);
