using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal sealed class ResoniteSceneBootstrapCoordinator : IResoniteSceneBootstrapCoordinator
{
    private readonly Func<IResoniteLinkClient, string, CancellationToken, Task<(CreatedSlot Slot, bool Existed)>> getOrCreateDatasetRootAsync;
    private readonly Func<IResoniteLinkClient, CreatedSlot, string, ResoniteFloat3?, ResoniteFloatQ?, CancellationToken, Task<CreatedSlot>> getOrCreateSharedChildSlotAsync;
    private readonly Func<IResoniteLinkClient, string, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task<CreatedComponent>> createComponentAsync;
    private readonly Func<IResoniteLinkClient, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task> updateComponentAsync;
    private readonly IResoniteSceneAnchorResolver sceneAnchorResolver;

    internal ResoniteSceneBootstrapCoordinator(
        Func<IResoniteLinkClient, string, CancellationToken, Task<(CreatedSlot Slot, bool Existed)>> getOrCreateDatasetRootAsync,
        Func<IResoniteLinkClient, CreatedSlot, string, ResoniteFloat3?, ResoniteFloatQ?, CancellationToken, Task<CreatedSlot>> getOrCreateSharedChildSlotAsync,
        Func<IResoniteLinkClient, string, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task<CreatedComponent>> createComponentAsync,
        Func<IResoniteLinkClient, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task> updateComponentAsync,
        IResoniteSceneAnchorResolver sceneAnchorResolver)
    {
        this.getOrCreateDatasetRootAsync = getOrCreateDatasetRootAsync;
        this.getOrCreateSharedChildSlotAsync = getOrCreateSharedChildSlotAsync;
        this.createComponentAsync = createComponentAsync;
        this.updateComponentAsync = updateComponentAsync;
        this.sceneAnchorResolver = sceneAnchorResolver;
    }

    public async Task<ResoniteSceneBootstrapState> BootstrapAsync(
        IResoniteLinkClient setupClient,
        ResoniteConstructionMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setupClient);
        ArgumentNullException.ThrowIfNull(metadata);

        string completionMeshCode = ResoniteSourceMeshCodeAnchor.ResolveCompletionMeshCode(metadata);
        (CreatedSlot datasetRootSlot, bool datasetRootExisted) = await getOrCreateDatasetRootAsync(
            setupClient,
            $"PLATEAU {metadata.Request.Dataset}",
            cancellationToken);
        CreatedSlot datasetAssetsRootSlot = await getOrCreateSharedChildSlotAsync(
            setupClient,
            datasetRootSlot,
            "Assets",
            null,
            null,
            cancellationToken);
        CreatedSlot commonAssetsRootSlot = await getOrCreateSharedChildSlotAsync(
            setupClient,
            datasetAssetsRootSlot,
            "Common",
            null,
            null,
            cancellationToken);
        SceneAnchor sceneAnchor = datasetRootExisted
            ? await sceneAnchorResolver.ResolveAsync(
                setupClient,
                datasetRootSlot.SlotId,
                completionMeshCode,
                datasetRootExisted,
                cancellationToken)
            : await CreateInitialSceneAnchorAsync(
                setupClient,
                datasetRootSlot,
                completionMeshCode,
                cancellationToken);

        return new ResoniteSceneBootstrapState(
            datasetRootSlot,
            datasetAssetsRootSlot,
            commonAssetsRootSlot,
            datasetRootExisted,
            sceneAnchor);
    }

    public async Task<string> ApplyDatasetLicenseAsync(
        IResoniteLinkClient setupClient,
        string datasetRootSlotId,
        ResoniteLicenseComponentMetadata license,
        string? existingComponentId,
        bool allowUpdateExisting,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setupClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRootSlotId);
        ArgumentNullException.ThrowIfNull(license);

        IReadOnlyDictionary<string, Member> members = CreateDatasetLicenseMembers(license);
        if (!string.IsNullOrWhiteSpace(existingComponentId))
        {
            await updateComponentAsync(
                setupClient,
                existingComponentId,
                members,
                cancellationToken);
            return existingComponentId;
        }

        if (allowUpdateExisting)
        {
            Slot? datasetRootSlot = await setupClient.GetSlotAsync(datasetRootSlotId, 0, cancellationToken);
            Component? existingLicenseComponent = datasetRootSlot?.Components?
                .Where(static component => string.Equals(
                    component.ComponentType,
                    "[FrooxEngine]FrooxEngine.License",
                    StringComparison.Ordinal))
                .OrderBy(static component => component.ID, StringComparer.Ordinal)
                .FirstOrDefault();
            if (existingLicenseComponent is not null && !string.IsNullOrWhiteSpace(existingLicenseComponent.ID))
            {
                await updateComponentAsync(
                    setupClient,
                    existingLicenseComponent.ID,
                    members,
                    cancellationToken);
                return existingLicenseComponent.ID;
            }
        }

        CreatedComponent createdComponent = await createComponentAsync(
            setupClient,
            datasetRootSlotId,
            "[FrooxEngine]FrooxEngine.License",
            members,
            cancellationToken);
        return createdComponent.ComponentId;
    }

    private async Task<SceneAnchor> CreateInitialSceneAnchorAsync(
        IResoniteLinkClient setupClient,
        CreatedSlot datasetRootSlot,
        string completionMeshCode,
        CancellationToken cancellationToken)
    {
        ResoniteFloat3 anchorPosition = new(0.0, 0.0, 0.0);
        CreatedSlot anchorSlot = await getOrCreateSharedChildSlotAsync(
            setupClient,
            datasetRootSlot,
            completionMeshCode,
            anchorPosition,
            null,
            cancellationToken);
        return new SceneAnchor(anchorSlot.SlotId, completionMeshCode, anchorPosition);
    }
    private static Dictionary<string, Member> CreateDatasetLicenseMembers(
        ResoniteLicenseComponentMetadata license)
    {
        return new Dictionary<string, Member>(StringComparer.Ordinal)
        {
            ["RequireCredit"] = new Field_bool
            {
                Value = license.RequireCredit,
            },
            ["CreditString"] = new Field_string
            {
                Value = $"{license.CreditText} License: {license.LicenseName} ({license.LicenseUrl})",
            },
        };
    }
}
