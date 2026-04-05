namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteConstructionMetadata(
    string SchemaVersion,
    string WorldName,
    PlateauImportRequest Request,
    PlateauSourceDataset SourceDataset,
    ResoniteAttribution Attribution,
    ResoniteLocalOrigin LocalOrigin);
