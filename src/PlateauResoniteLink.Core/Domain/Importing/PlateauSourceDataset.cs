using System.Collections.Generic;

namespace PlateauResoniteLink.Domain.Importing;

public sealed record PlateauSourceDataset(
    IReadOnlyList<string> PackageNames,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string>? SelectedMeshCodes = null,
    IReadOnlyDictionary<string, string>? SourceFilePackageNamesByRelativePath = null);
