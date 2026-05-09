namespace PlateauResoniteLink.Application.Importing;

internal interface IDefaultMaterialResolver
{
    ResolvedMaterial ResolveMaterial(DefaultMaterialRequest request);
}
