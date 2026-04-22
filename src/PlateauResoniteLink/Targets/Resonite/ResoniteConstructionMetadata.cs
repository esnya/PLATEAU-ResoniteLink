namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteConstructionMetadata(
    string SchemaVersion,
    string WorldName,
    ResoniteImportRequest Request,
    ResoniteSourceDataset SourceDataset,
    ResoniteAttribution Attribution,
    ResoniteLocalOrigin LocalOrigin);
