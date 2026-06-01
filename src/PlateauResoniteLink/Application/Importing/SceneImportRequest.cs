namespace PlateauResoniteLink.Application.Importing;

public sealed record SceneImportRequest(
    ImportedSceneMetadata Metadata,
    string WorkRoot,
    CommonMaterialCatalog<DefaultCommonMaterialMember> CommonMaterials);
