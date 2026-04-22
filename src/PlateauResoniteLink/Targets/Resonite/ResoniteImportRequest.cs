namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteImportRequest(
    string Dataset,
    string MeshCode,
    string? LocalSourcePath);
