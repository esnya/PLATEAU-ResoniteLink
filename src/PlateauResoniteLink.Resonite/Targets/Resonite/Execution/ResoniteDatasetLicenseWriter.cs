using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Resonite.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Resonite.Targets.Resonite.Execution;

internal static class ResoniteDatasetLicenseWriter
{
    private const string LicenseComponentType = "[FrooxEngine]FrooxEngine.License";
    private const string GsiLicenseName = "GSI Maps Terms";
    private const string GsiLicenseUrl = "https://maps.gsi.go.jp/help/termsofuse.html";

    public static async Task EnsureGsiFallbackLicenseAsync(
        IResoniteLinkClient client,
        CreatedSlot datasetRootSlot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        Slot datasetRootSnapshot = await client.GetSlotAsync(
                new ResoniteTransportSlotLocator(datasetRootSlot.Locator.Value),
                1,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"ResoniteLink did not surface dataset root '{datasetRootSlot.Locator.Value}' while ensuring the GSI fallback license.");
        if (HasGsiFallbackLicense(datasetRootSnapshot))
        {
            return;
        }

        _ = await client.RunDataModelOperationBatchAsync(
            [
                ResoniteBatchOperations.CreateAddComponentOperation(
                    datasetRootSlot.Locator.Value,
                    LicenseComponentType,
                    CreateGsiFallbackLicenseMembers()),
            ],
            cancellationToken);
    }

    private static bool HasGsiFallbackLicense(Slot datasetRootSnapshot)
    {
        foreach (Component component in datasetRootSnapshot.Components ?? [])
        {
            if (!string.Equals(component.ComponentType, LicenseComponentType, StringComparison.Ordinal)
                || !component.Members.TryGetValue("CreditString", out Member? member)
                || member is not Field_string creditString)
            {
                continue;
            }

            if (creditString.Value.Contains(GsiLicenseUrl, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, Member> CreateGsiFallbackLicenseMembers()
    {
        return new Dictionary<string, Member>(StringComparer.Ordinal)
        {
            ["RequireCredit"] = new Field_bool
            {
                Value = true,
            },
            ["CreditString"] = new Field_string
            {
                Value = $"DEM terrain imagery may use fallback to GSI seamless photo tiles where PLATEAU-Ortho coverage is unavailable. License: {GsiLicenseName} ({GsiLicenseUrl})",
            },
        };
    }
}
