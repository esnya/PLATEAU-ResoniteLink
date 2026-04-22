using System;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlConstructionComposer(
    ICityGmlGeometryProjector geometryProjector,
    IDemTextureSourcePolicy demTextureSourcePolicy) : IImportedSceneSourceComposer
{
    private readonly ICityGmlGeometryProjector geometryProjector = geometryProjector;
    private readonly IDemTextureSourcePolicy demTextureSourcePolicy = demTextureSourcePolicy;
    private const string PlateauLicenseName = "PLATEAU Open Data Terms";
    private const string PlateauLicenseUrl = "https://www.mlit.go.jp/plateau/site-policy/";

    public IImportedSceneSource Compose(
        PlateauImportRequest request,
        LocalCityGmlBootstrapSnapshot readResult,
        Action<string>? progressReporter = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(readResult);
        LocalCityGmlDocumentSet documentSet = readResult.DocumentSet;
        LocalCityGmlBootstrapContext bootstrapContext = readResult.BootstrapContext;

        ImportedSceneMetadata metadata = new(
            SchemaVersion: "3.0",
            SceneName: $"PLATEAU {request.Dataset} {request.MeshCode}",
            Request: request,
            SourceDataset: new PlateauSourceDataset(
                PackageNames: documentSet.PackageNames.ToArray(),
                SourceFiles: documentSet.RelativeSourceFiles.ToArray(),
                SelectedMeshCodes: documentSet.SelectedMeshCodes),
            Attribution: CreateAttribution(request),
            GeodeticOrigin: new GeodeticOrigin(
                Latitude: bootstrapContext.GlobalOriginPoint.Latitude,
                Longitude: bootstrapContext.GlobalOriginPoint.Longitude,
                Altitude: bootstrapContext.GlobalOriginPoint.Altitude));

        return new LocalCityGmlConstructionSource(
            metadata,
            request,
            readResult,
            geometryProjector,
            demTextureSourcePolicy,
            progressReporter);
    }

    private static Attribution CreateAttribution(PlateauImportRequest request)
    {
        return new Attribution(
            DatasetLicense: new LicenseMetadata(
                RequireCredit: true,
                CreditText: $"Contains PLATEAU dataset content for {request.Dataset}. Follow the original PLATEAU dataset terms and provide source attribution when redistributing derived content.",
                LicenseName: PlateauLicenseName,
                LicenseUrl: PlateauLicenseUrl),
            MaterialLicenses: []);
    }
}
