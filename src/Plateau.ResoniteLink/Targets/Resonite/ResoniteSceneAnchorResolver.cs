using GeographicLib;

using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Targets.Resonite;

internal interface IResoniteSceneAnchorResolver
{
    Task<SceneAnchor> ResolveAsync(
        IResoniteLinkClient client,
        string datasetRootSlotId,
        string completionMeshCode,
        bool datasetRootExisted,
        CancellationToken cancellationToken);
}

internal readonly record struct SceneAnchor(
    string LocationSlotId,
    string MeshCode,
    ResoniteFloat3 Position,
    string? ReferenceSourceFileRootId);

internal sealed class ResoniteSceneAnchorResolver : IResoniteSceneAnchorResolver
{
    private const string RootSlotId = "Root";

    public async Task<SceneAnchor> ResolveAsync(
        IResoniteLinkClient client,
        string datasetRootSlotId,
        string completionMeshCode,
        bool datasetRootExisted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRootSlotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(completionMeshCode);

        await WaitForSlotAvailableAsync(client, datasetRootSlotId, cancellationToken);
        ResoniteSceneSlotSnapshot datasetRootSnapshot = await ResoniteSceneSlotSnapshot.CreateAsync(
            client,
            datasetRootSlotId,
            1,
            cancellationToken);
        Slot[] sourceFileRoots = datasetRootSnapshot.Root?.Children?
            .Where(static child => !string.Equals(child.Name?.Value, "Assets", StringComparison.Ordinal))
            .Where(static child => ResoniteSourceMeshCodeAnchor.TryGetConcreteMeshCode(child.Name?.Value ?? string.Empty, out _))
            .ToArray()
            ?? [];
        Slot? completionSourceFileRoot = sourceFileRoots.FirstOrDefault(
            child => ResoniteSourceMeshCodeAnchor.TryGetConcreteMeshCode(child.Name?.Value ?? string.Empty, out string meshCode)
                && string.Equals(meshCode, completionMeshCode, StringComparison.Ordinal));
        if (completionSourceFileRoot is not null)
        {
            return new SceneAnchor(
                completionSourceFileRoot.ID ?? datasetRootSlotId,
                completionMeshCode,
                GetSlotPositionOrDefault(completionSourceFileRoot),
                completionSourceFileRoot.ID);
        }

        (Slot Slot, string MeshCode)? referenceSourceFileRoot = sourceFileRoots
            .Select(static child => ResoniteSourceMeshCodeAnchor.TryGetConcreteMeshCode(child.Name?.Value ?? string.Empty, out string meshCode)
                ? (Slot: child, MeshCode: meshCode)
                : ((Slot Slot, string MeshCode)?)null)
            .Where(static candidate => candidate.HasValue)
            .Select(static candidate => candidate!.Value)
            .OrderBy(static candidate => candidate.MeshCode, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Slot.ID ?? string.Empty, StringComparer.Ordinal)
            .FirstOrDefault();
        if (referenceSourceFileRoot is not null)
        {
            return new SceneAnchor(
                referenceSourceFileRoot.Value.Slot.ID ?? datasetRootSlotId,
                completionMeshCode,
                Add(
                    GetSlotPositionOrDefault(referenceSourceFileRoot.Value.Slot),
                    ComputeMeshCodeOffset(referenceSourceFileRoot.Value.MeshCode, completionMeshCode)),
                referenceSourceFileRoot.Value.Slot.ID);
        }

        throw new InvalidOperationException(
            $"Existing dataset root '{datasetRootSlotId}' does not contain a positioned source-file root that can resolve completion mesh '{completionMeshCode}'.");
    }

    private static async Task WaitForSlotAvailableAsync(
        IResoniteLinkClient client,
        string slotId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(slotId, RootSlotId, StringComparison.Ordinal))
        {
            return;
        }

        Slot? slot = await client.GetSlotAsync(slotId, 0, cancellationToken);
        if (slot is not null)
        {
            return;
        }

        throw new InvalidOperationException($"ResoniteLink did not surface slot '{slotId}' on the initial probe.");
    }

    private static ResoniteFloat3 GetSlotPositionOrDefault(Slot slot)
    {
        if (slot.Position is Field_float3 position)
        {
            return new ResoniteFloat3(position.Value.x, position.Value.y, position.Value.z);
        }

        return new ResoniteFloat3(0.0, 0.0, 0.0);
    }

    private static ResoniteFloat3 ComputeMeshCodeOffset(string referenceMeshCode, string meshCode)
    {
        if (!PlateauMeshCode.TryGetCenter(referenceMeshCode, out ResoniteLocalOrigin referenceCenter)
            || !PlateauMeshCode.TryGetCenter(meshCode, out ResoniteLocalOrigin currentCenter))
        {
            return new ResoniteFloat3(0.0, 0.0, 0.0);
        }

        return ComputeOriginOffset(referenceCenter, currentCenter);
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

    private static ResoniteFloat3 Add(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

}
