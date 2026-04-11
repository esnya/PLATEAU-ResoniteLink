namespace Plateau.ResoniteLink.Application.Importing;

public interface IDefaultMaterialResolver
{
    ResolvedMaterial ResolveMaterial(
        string packageName,
        string? texturePath,
        bool preferUvProjection,
        string? familyOverride,
        string variantSelectionKey);
}
