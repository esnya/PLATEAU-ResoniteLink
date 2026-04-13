using GeographicLib;

using System.Globalization;

using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal sealed class ResoniteSceneBootstrapCoordinator : IResoniteSceneBootstrapCoordinator
{
    private readonly Func<IResoniteLinkClient, string, CancellationToken, Task<(ResoniteLinkSceneBuilder.CreatedSlot Slot, bool Existed)>> getOrCreateDatasetRootAsync;
    private readonly Func<IResoniteLinkClient, ResoniteLinkSceneBuilder.CreatedSlot, string, ResoniteFloat3?, ResoniteFloatQ?, CancellationToken, Task<ResoniteLinkSceneBuilder.CreatedSlot>> getOrCreateSharedChildSlotAsync;
    private readonly Func<IResoniteLinkClient, string, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task<ResoniteLinkSceneBuilder.CreatedComponent>> createComponentAsync;
    private readonly IResoniteSceneAnchorResolver sceneAnchorResolver;

    internal ResoniteSceneBootstrapCoordinator(
        Func<IResoniteLinkClient, string, CancellationToken, Task<(ResoniteLinkSceneBuilder.CreatedSlot Slot, bool Existed)>> getOrCreateDatasetRootAsync,
        Func<IResoniteLinkClient, ResoniteLinkSceneBuilder.CreatedSlot, string, ResoniteFloat3?, ResoniteFloatQ?, CancellationToken, Task<ResoniteLinkSceneBuilder.CreatedSlot>> getOrCreateSharedChildSlotAsync,
        Func<IResoniteLinkClient, string, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task<ResoniteLinkSceneBuilder.CreatedComponent>> createComponentAsync,
        IResoniteSceneAnchorResolver sceneAnchorResolver)
    {
        this.getOrCreateDatasetRootAsync = getOrCreateDatasetRootAsync;
        this.getOrCreateSharedChildSlotAsync = getOrCreateSharedChildSlotAsync;
        this.createComponentAsync = createComponentAsync;
        this.sceneAnchorResolver = sceneAnchorResolver;
    }

    public async Task<ResoniteSceneBootstrapState> BootstrapAsync(
        IResoniteLinkClient setupClient,
        ResoniteConstructionMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setupClient);
        ArgumentNullException.ThrowIfNull(metadata);

        string completionMeshCode = ResolveCompletionMeshCode(metadata);
        (ResoniteLinkSceneBuilder.CreatedSlot datasetRootSlot, bool datasetRootExisted) = await getOrCreateDatasetRootAsync(
            setupClient,
            $"PLATEAU {metadata.Request.Dataset}",
            cancellationToken);
        ResoniteLinkSceneBuilder.CreatedSlot datasetAssetsRootSlot = await getOrCreateSharedChildSlotAsync(
            setupClient,
            datasetRootSlot,
            "Assets",
            null,
            null,
            cancellationToken);
        ResoniteLinkSceneBuilder.CreatedSlot commonAssetsRootSlot = await getOrCreateSharedChildSlotAsync(
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

    public async Task ApplyDatasetLicenseAsync(
        IResoniteLinkClient setupClient,
        string datasetRootSlotId,
        ResoniteLicenseComponentMetadata license,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setupClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRootSlotId);
        ArgumentNullException.ThrowIfNull(license);

        _ = await createComponentAsync(
            setupClient,
            datasetRootSlotId,
            "[FrooxEngine]FrooxEngine.License",
            CreateDatasetLicenseMembers(license),
            cancellationToken);
    }

    private async Task<SceneAnchor> CreateInitialSceneAnchorAsync(
        IResoniteLinkClient setupClient,
        ResoniteLinkSceneBuilder.CreatedSlot datasetRootSlot,
        string completionMeshCode,
        CancellationToken cancellationToken)
    {
        ResoniteFloat3 anchorPosition = new(0.0, 0.0, 0.0);
        ResoniteLinkSceneBuilder.CreatedSlot anchorSlot = await getOrCreateSharedChildSlotAsync(
            setupClient,
            datasetRootSlot,
            completionMeshCode,
            anchorPosition,
            null,
            cancellationToken);
        return new SceneAnchor(anchorSlot.SlotId, completionMeshCode, anchorPosition);
    }

    private static string ResolveCompletionMeshCode(ResoniteConstructionMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        string meshCode = metadata.Request.MeshCode;
        if (PlateauMeshCode.TryGetCenter(meshCode, out _))
        {
            return meshCode;
        }

        (string MeshCode, double DistanceSquared)[] concreteRequestedMeshCodes = metadata.SourceDataset.RequestedMeshCodes?
            .Select(candidate => TryResolveConcreteMeshCodeDistance(candidate, metadata.LocalOrigin))
            .Where(static candidate => candidate.HasValue)
            .Select(static candidate => candidate!.Value)
            .OrderBy(static candidate => candidate.DistanceSquared)
            .ThenBy(static candidate => candidate.MeshCode, StringComparer.Ordinal)
            .ToArray()
            ?? [];
        if (concreteRequestedMeshCodes.Length > 0)
        {
            return concreteRequestedMeshCodes[0].MeshCode;
        }

        throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Live Offset V2 requires a concrete meshcode anchor, but '{meshCode}' did not resolve to any concrete meshcode."));
    }

    private static (string MeshCode, double DistanceSquared)? TryResolveConcreteMeshCodeDistance(
        string meshCode,
        ResoniteLocalOrigin requestOrigin)
    {
        if (!PlateauMeshCode.TryGetCenter(meshCode, out ResoniteLocalOrigin concreteCenter))
        {
            return null;
        }

        ResoniteFloat3 offset = ComputeOriginOffset(requestOrigin, concreteCenter);
        double distanceSquared = (offset.X * offset.X) + (offset.Z * offset.Z);
        return (meshCode, distanceSquared);
    }

    private static ResoniteFloat3 ComputeOriginOffset(
        ResoniteLocalOrigin referenceCenter,
        ResoniteLocalOrigin currentCenter)
    {
        LocalCartesian cartesian = new(
            referenceCenter.Latitude,
            referenceCenter.Longitude,
            referenceCenter.Altitude,
            Geocentric.WGS84);
        (double x, double y, double z) eun = cartesian.Forward(
            currentCenter.Latitude,
            currentCenter.Longitude,
            currentCenter.Altitude);
        return new ResoniteFloat3(
            X: eun.x,
            Y: 0.0,
            Z: eun.y);
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
