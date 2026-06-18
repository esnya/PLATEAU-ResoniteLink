using PlateauResoniteLink.Core.Application.Importing.Contracts;
namespace PlateauResoniteLink.Core.Application.Importing;

public sealed record SceneImportRequest(
    ImportedSceneMetadata Metadata,
    string WorkRoot,
    CommonMaterialCatalog<DefaultCommonMaterialMember> CommonMaterials);
