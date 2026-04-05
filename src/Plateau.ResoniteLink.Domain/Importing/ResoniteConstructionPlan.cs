namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteConstructionPlan(
    string SchemaVersion,
    string WorldName,
    PlateauImportRequest Request,
    PlateauSourceDataset SourceDataset,
    ResoniteAttribution Attribution,
    ResoniteLocalOrigin LocalOrigin,
    IReadOnlyList<ResoniteConstructionCityObject> CityObjects);
