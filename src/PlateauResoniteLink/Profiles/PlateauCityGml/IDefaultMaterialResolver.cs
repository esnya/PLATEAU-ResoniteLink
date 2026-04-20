using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public interface IDefaultMaterialResolver
{
    ResolvedMaterial ResolveMaterial(
        string packageName,
        ResoniteTexturePayload? texturePayload,
        bool preferUvProjection,
        string? familyOverride,
        string variantSelectionKey);
}
