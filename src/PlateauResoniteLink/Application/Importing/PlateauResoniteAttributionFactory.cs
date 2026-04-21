using System;

using System.Globalization;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class PlateauResoniteAttributionFactory
{
    private const string PlateauLicenseName = "PLATEAU Open Data Terms";
    private const string PlateauLicenseUrl = "https://www.mlit.go.jp/plateau/site-policy/";

    public static ResoniteAttribution Create(PlateauImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ResoniteAttribution(
            DatasetLicense: new LicenseAttributionMetadata(
                RequireCredit: true,
                CreditText: string.Create(
                    CultureInfo.InvariantCulture,
                    $"Contains PLATEAU dataset content for {request.Dataset}. Follow the original PLATEAU dataset terms and provide source attribution when redistributing derived content."),
                LicenseName: PlateauLicenseName,
                LicenseUrl: PlateauLicenseUrl),
            MaterialLicenses: []);
    }
}
