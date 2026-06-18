using PlateauResoniteLink.Application.Importing.Source;
namespace PlateauResoniteLink.Application.Importing.Plateau;

internal interface IDefaultMaterialResolver
{
    ResolvedMaterial ResolveMaterial(DefaultMaterialRequest request);
}
