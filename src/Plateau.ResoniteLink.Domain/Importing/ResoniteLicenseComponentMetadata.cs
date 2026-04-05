namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteLicenseComponentMetadata(
    bool RequireCredit,
    string CreditText,
    string LicenseName,
    string LicenseUrl);
