namespace PlateauResoniteLink.Application.Importing;

internal interface IDefaultMaterialResolver
{
    ResolvedMaterial ResolveMaterial(
        string packageName,
        TexturePayload? texturePayload,
        bool preferUvProjection,
        string? familyOverride,
        string variantSelectionKey);
}
