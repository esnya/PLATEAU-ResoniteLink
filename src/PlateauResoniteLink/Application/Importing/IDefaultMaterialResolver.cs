using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal interface IDefaultMaterialResolver
{
    ResolvedMaterial ResolveMaterial(
        string packageName,
        ResoniteTexturePayload? texturePayload,
        bool preferUvProjection,
        string? familyOverride,
        string variantSelectionKey);
}
