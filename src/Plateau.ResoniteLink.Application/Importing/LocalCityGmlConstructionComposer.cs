using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class LocalCityGmlConstructionComposer(ICityGmlGeometryProjector geometryProjector) : IResoniteConstructionComposer
{
    private readonly ICityGmlGeometryProjector geometryProjector = geometryProjector;

    public IResoniteConstructionSource Compose(
        PlateauImportRequest request,
        LocalCityGmlDocumentSet documentSet)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(documentSet);

        ResoniteAttribution attribution = PlateauResoniteAttributionFactory.Create(request);
        ResoniteConstructionMetadata metadata = new(
            SchemaVersion: "3.0",
            WorldName: $"PLATEAU {request.Dataset} {request.MeshCode}",
            Request: request,
            SourceDataset: new PlateauSourceDataset(
                PackageNames: documentSet.PackageNames.ToArray(),
                SourceFiles: documentSet.RelativeSourceFiles.ToArray(),
                TerrainTextureOverlays: documentSet.TerrainTextureOverlays.ToArray(),
                RequestedMeshCodes: documentSet.RequestedMeshCodes),
            Attribution: attribution,
            LocalOrigin: new ResoniteLocalOrigin(
                Latitude: documentSet.GlobalOriginPoint.Latitude,
                Longitude: documentSet.GlobalOriginPoint.Longitude,
                Altitude: documentSet.GlobalOriginPoint.Altitude));

        return new LocalCityGmlConstructionSource(
            metadata,
            request,
            documentSet,
            geometryProjector);
    }
}
