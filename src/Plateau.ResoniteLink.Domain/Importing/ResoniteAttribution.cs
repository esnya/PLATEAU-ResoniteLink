#pragma warning disable IDE0032

namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteAttribution
{
    private ResoniteLicenseComponentMetadata datasetLicense = null!;
    private IReadOnlyList<ResoniteMaterialAttribution> materialLicenses = Array.Empty<ResoniteMaterialAttribution>();

    public ResoniteAttribution(
        ResoniteLicenseComponentMetadata DatasetLicense,
        IReadOnlyList<ResoniteMaterialAttribution> MaterialLicenses)
    {
        this.DatasetLicense = DatasetLicense;
        this.MaterialLicenses = MaterialLicenses;
    }

    public ResoniteLicenseComponentMetadata DatasetLicense
    {
        get => datasetLicense;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            datasetLicense = value;
        }
    }

    public IReadOnlyList<ResoniteMaterialAttribution> MaterialLicenses
    {
        get => materialLicenses;
        init => materialLicenses = CollectionCopy.List(value, nameof(MaterialLicenses));
    }

    public void Deconstruct(
        out ResoniteLicenseComponentMetadata DatasetLicense,
        out IReadOnlyList<ResoniteMaterialAttribution> MaterialLicenses)
    {
        DatasetLicense = this.DatasetLicense;
        MaterialLicenses = this.MaterialLicenses;
    }
}

#pragma warning restore IDE0032
