namespace PlateauResoniteLink.Application.Importing;

public sealed record SceneImportRequest(
    ImportedSceneMetadata Metadata,
    string ResolvedSourcePath,
    string WorkRoot,
    CommonMaterialCatalogSnapshot CommonMaterials);
