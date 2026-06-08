using System;
using System.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class DefaultImportedSceneSourceComposer(
    CityGmlGeometryProjector geometryProjector,
    ResolveDemTextureSources resolveDemTextureSources)
{
    private readonly CityGmlGeometryProjector geometryProjector = geometryProjector;
    private readonly ResolveDemTextureSources resolveDemTextureSources = resolveDemTextureSources;
    private const string PlateauLicenseName = "PLATEAU Open Data Terms";
    private const string PlateauLicenseUrl = "https://www.mlit.go.jp/plateau/site-policy/";

    public IImportedSceneSource Compose(
        ResolvedLocalPlateauImportRequest request,
        ImportedSceneSourceSnapshot readResult,
        ImportedObjectUnitOptimizer objectUnitOptimizer,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(readResult);
        ArgumentNullException.ThrowIfNull(objectUnitOptimizer);
        ImportedSceneSourceDataset documentSet = readResult.DocumentSet;
        ImportedSceneSourceContext discoveryContext = readResult.DiscoveryContext;
        PlateauImportRequest importRequest = request.ToImportRequest();

        ImportedSceneMetadata metadata = new(
            SchemaVersion: "3.0",
            SceneName: $"PLATEAU {request.Dataset} {request.MeshCode}",
            Request: importRequest,
            SourceDataset: new PlateauSourceDataset(
                PackageNames: documentSet.PackageNames.ToArray(),
                SourceFiles: documentSet.RelativeSourceFiles.ToArray(),
                SelectedMeshCodes: documentSet.SelectedMeshCodes),
            Attribution: CreateAttribution(importRequest),
            GeodeticOrigin: new GeodeticOrigin(
                Latitude: discoveryContext.GlobalOriginPoint.Latitude,
                Longitude: discoveryContext.GlobalOriginPoint.Longitude,
                Altitude: discoveryContext.GlobalOriginPoint.Altitude));

        return new StreamingImportedSceneSource(
            metadata,
            importRequest,
            readResult,
            geometryProjector,
            resolveDemTextureSources,
            objectUnitOptimizer,
            loggerFactory ?? NullLoggerFactory.Instance);
    }

    private static Attribution CreateAttribution(PlateauImportRequest request)
    {
        return new Attribution(
            DatasetLicense: new LicenseMetadata(
                RequireCredit: true,
                CreditText: $"Contains PLATEAU dataset content for {request.Dataset}. Follow the original PLATEAU dataset terms and provide source attribution when redistributing derived content.",
                LicenseName: PlateauLicenseName,
                LicenseUrl: PlateauLicenseUrl));
    }
}
