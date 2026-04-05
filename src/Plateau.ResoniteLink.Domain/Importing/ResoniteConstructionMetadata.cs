namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteConstructionMetadata(
    string SchemaVersion,
    string WorldName,
    PlateauImportRequest Request,
    PlateauSourceDataset SourceDataset,
    ResoniteLocalOrigin LocalOrigin)
{
    public ResoniteConstructionPlan ToPlan(IReadOnlyList<ResoniteConstructionCityObject> cityObjects)
    {
        ArgumentNullException.ThrowIfNull(cityObjects);

        return new ResoniteConstructionPlan(
            SchemaVersion,
            WorldName,
            Request,
            SourceDataset,
            LocalOrigin,
            cityObjects);
    }
}
