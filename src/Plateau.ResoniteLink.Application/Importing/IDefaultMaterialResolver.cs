using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public interface IDefaultMaterialResolver
{
    ResolvedMaterial ResolveMaterial(
        string packageName,
        ResoniteTexturePayload? texturePayload,
        bool preferUvProjection,
        string? familyOverride,
        string variantSelectionKey);
}
