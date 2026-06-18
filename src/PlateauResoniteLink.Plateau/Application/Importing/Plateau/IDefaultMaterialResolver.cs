using PlateauResoniteLink.Plateau.Application.Importing.Source;
namespace PlateauResoniteLink.Plateau.Application.Importing.Plateau;

internal interface IDefaultMaterialResolver
{
    ResolvedMaterial ResolveMaterial(DefaultMaterialRequest request);
}
