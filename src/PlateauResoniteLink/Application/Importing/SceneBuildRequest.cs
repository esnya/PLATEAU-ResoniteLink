using System.Collections.Generic;

namespace PlateauResoniteLink.Application.Importing;

public sealed record SceneBuildRequest(
    ImportedSceneMetadata Metadata,
    string ResolvedSourcePath,
    string WorkRoot,
    IReadOnlyList<MaterialBinding> CommonMaterials);
