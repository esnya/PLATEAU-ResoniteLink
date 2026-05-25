namespace PlateauResoniteLink.Targets.Resonite;

internal readonly record struct NonDemSourceFileBatchKey(
    string ActualMeshCode,
    string PackageName,
    int? LodLevel,
    string PolicyContext,
    string SourceFileRelativePath);
