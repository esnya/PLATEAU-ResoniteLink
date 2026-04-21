using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed record ResoniteConstructionMetadata(
    string SchemaVersion,
    string WorldName,
    PlateauImportRequest Request,
    PlateauSourceDataset SourceDataset,
    ResoniteAttribution Attribution,
    ResoniteLocalOrigin LocalOrigin);
