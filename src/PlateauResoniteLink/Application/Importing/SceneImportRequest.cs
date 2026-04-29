using System.Collections.Generic;

namespace PlateauResoniteLink.Application.Importing;

public sealed record SceneImportRequest(
    ImportedSceneMetadata Metadata,
    string ResolvedSourcePath,
    string WorkRoot,
    IReadOnlyList<MaterialBinding> CommonMaterials);
