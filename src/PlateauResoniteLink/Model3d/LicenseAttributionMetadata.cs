namespace PlateauResoniteLink.Domain.Importing;

public sealed record LicenseAttributionMetadata(
    bool RequireCredit,
    string CreditText,
    string LicenseName,
    string LicenseUrl);
