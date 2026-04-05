using System.Globalization;

using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal sealed class ResoniteLicenseManager(ResoniteAttribution attribution)
{
    private readonly Dictionary<string, ResoniteMaterialAttribution> materialLicenses =
        attribution.MaterialLicenses.ToDictionary(static item => item.MaterialKey, StringComparer.Ordinal);

    public async Task EnsureDatasetLicenseAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerSlotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);

        Dictionary<string, Member> members = CreateLicenseMembers(attribution.DatasetLicense);
        Component? existingComponent = await client.GetComponentAsync(componentId, cancellationToken);
        if (existingComponent is null)
        {
            await client.AddComponentAsync(
                new AddComponent
                {
                    ContainerSlotId = containerSlotId,
                    Data = new Component
                    {
                        ID = componentId,
                        ComponentType = "[FrooxEngine]FrooxEngine.License",
                        Members = new Dictionary<string, Member>(members, StringComparer.Ordinal),
                    },
                },
                cancellationToken);
            return;
        }

        await client.UpdateComponentAsync(
            new UpdateComponent
            {
                Data = new Component
                {
                    ID = componentId,
                    Members = new Dictionary<string, Member>(members, StringComparer.Ordinal),
                },
            },
            cancellationToken);
    }

    public ResoniteMaterialAttribution? GetMaterialAttribution(string materialKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialKey);
        materialLicenses.TryGetValue(materialKey, out ResoniteMaterialAttribution? attributionForMaterial);
        return attributionForMaterial;
    }

    private static Dictionary<string, Member> CreateLicenseMembers(ResoniteLicenseComponentMetadata license)
    {
        return new Dictionary<string, Member>(StringComparer.Ordinal)
        {
            ["RequireCredit"] = new Field_bool
            {
                Value = license.RequireCredit,
            },
            ["CreditString"] = new Field_string
            {
                Value = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{license.CreditText} License: {license.LicenseName} ({license.LicenseUrl})"),
            },
        };
    }
}
