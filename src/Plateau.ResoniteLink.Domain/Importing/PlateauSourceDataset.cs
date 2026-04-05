namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record PlateauSourceDataset(
    string PackageName,
    IReadOnlyList<string> SourceFiles);
