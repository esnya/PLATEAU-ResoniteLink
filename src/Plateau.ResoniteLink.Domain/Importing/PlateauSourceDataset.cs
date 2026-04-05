namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record PlateauSourceDataset(
    IReadOnlyList<string> PackageNames,
    IReadOnlyList<string> SourceFiles);
