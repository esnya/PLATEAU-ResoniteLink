using System;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlConstructionComposer(
    ICityGmlGeometryProjector geometryProjector,
    ICityGmlCommonMaterialEnumerator commonMaterialEnumerator,
    IDemTextureSourcePolicy demTextureSourcePolicy) : IImportedSceneSourceComposer
{
    private readonly ICityGmlGeometryProjector geometryProjector = geometryProjector;
    private readonly ICityGmlCommonMaterialEnumerator commonMaterialEnumerator = commonMaterialEnumerator;
    private readonly IDemTextureSourcePolicy demTextureSourcePolicy = demTextureSourcePolicy;

    public IImportedSceneSource Compose(
        PlateauImportRequest request,
        LocalCityGmlDocumentReadResult readResult,
        Action<string>? progressReporter = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(readResult);
        LocalCityGmlDocumentSet documentSet = readResult.DocumentSet;
        LocalCityGmlBootstrapContext bootstrapContext = readResult.BootstrapContext;
        ResoniteAttribution attribution = PlateauResoniteAttributionFactory.Create(request);

        ImportedSceneMetadata metadata = new(
            SchemaVersion: "3.0",
            SceneName: $"PLATEAU {request.Dataset} {request.MeshCode}",
            Request: request,
            SourceDataset: new PlateauSourceDataset(
                PackageNames: documentSet.PackageNames.ToArray(),
                SourceFiles: documentSet.RelativeSourceFiles.ToArray(),
                TerrainTextureOverlays: documentSet.TerrainTextureOverlays.ToArray(),
                SelectedMeshCodes: documentSet.SelectedMeshCodes),
            Attribution: new Attribution(
                DatasetLicense: new LicenseMetadata(
                    attribution.DatasetLicense.RequireCredit,
                    attribution.DatasetLicense.CreditText,
                    attribution.DatasetLicense.LicenseName,
                    attribution.DatasetLicense.LicenseUrl),
                MaterialLicenses: attribution.MaterialLicenses
                    .Select(static license => new MaterialAttribution(
                        license.MaterialKey,
                        license.License is null
                            ? null
                            : new LicenseMetadata(
                                license.License.RequireCredit,
                                license.License.CreditText,
                                license.License.LicenseName,
                                license.License.LicenseUrl)))
                    .ToArray()),
            GeodeticOrigin: new GeodeticOrigin(
                Latitude: bootstrapContext.GlobalOriginPoint.Latitude,
                Longitude: bootstrapContext.GlobalOriginPoint.Longitude,
                Altitude: bootstrapContext.GlobalOriginPoint.Altitude));

        return new LocalCityGmlConstructionSource(
            metadata,
            request,
            readResult,
            geometryProjector,
            commonMaterialEnumerator,
            demTextureSourcePolicy,
            progressReporter);
    }
}
