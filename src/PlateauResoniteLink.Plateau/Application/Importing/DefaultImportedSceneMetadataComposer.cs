using System;
using System.Linq;

using PlateauResoniteLink.Application.Importing.Contracts;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class DefaultImportedSceneMetadataComposer : IImportedSceneMetadataComposer
{
    private const string PlateauLicenseName = "PLATEAU Open Data Terms";
    private const string PlateauLicenseUrl = "https://www.mlit.go.jp/plateau/site-policy/";

    public ImportedSceneMetadata Compose(
        ResolvedLocalPlateauImportRequest request,
        ImportedSceneSourceSnapshot readResult)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(readResult);

        ImportedSceneSourceDataset documentSet = readResult.DocumentSet;
        ImportedSceneSourceContext discoveryContext = readResult.DiscoveryContext;
        PlateauImportRequest importRequest = request.ToImportRequest();

        return new ImportedSceneMetadata(
            SchemaVersion: "3.0",
            SceneName: $"PLATEAU {request.Dataset} {request.MeshCode}",
            Request: importRequest,
            SourceDataset: new PlateauSourceDataset(
                PackageNames: documentSet.PackageNames.ToArray(),
                SourceFiles: documentSet.RelativeSourceFiles.ToArray(),
                SelectedMeshCodes: documentSet.SelectedMeshCodes,
                SourceFilePackageNamesByRelativePath: PlateauSourceFilePackageIndex.CreateByRelativePath(documentSet.RelativeSourceFiles)),
            Attribution: CreateAttribution(importRequest),
            GeodeticOrigin: new GeodeticOrigin(
                Latitude: discoveryContext.GlobalOriginPoint.Latitude,
                Longitude: discoveryContext.GlobalOriginPoint.Longitude,
                Altitude: discoveryContext.GlobalOriginPoint.Altitude));
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
