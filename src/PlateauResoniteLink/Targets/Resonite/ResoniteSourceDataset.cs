using System.Collections.Generic;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteSourceDataset(
    IReadOnlyList<string> PackageNames,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string>? SelectedMeshCodes);
