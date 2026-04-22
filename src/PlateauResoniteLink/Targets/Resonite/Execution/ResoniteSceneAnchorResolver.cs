using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal interface IResoniteSceneAnchorResolver
{
    Task<SceneAnchor> ResolveAsync(
        IResoniteLinkClient client,
        ResoniteSlotLocator datasetRootSlot,
        string completionMeshCode,
        CancellationToken cancellationToken);
}

internal readonly record struct SceneAnchor(
    ResoniteSlotLocator LocationSlot,
    string MeshCode,
    ResoniteFloat3 Position,
    ResoniteSlotLocator? ReferenceSourceFileRoot);

internal sealed class ResoniteSceneAnchorResolver : IResoniteSceneAnchorResolver
{
    private const string RootSlotId = "Root";

    public async Task<SceneAnchor> ResolveAsync(
        IResoniteLinkClient client,
        ResoniteSlotLocator datasetRootSlot,
        string completionMeshCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRootSlot.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(completionMeshCode);

        await WaitForSlotAvailableAsync(client, datasetRootSlot, cancellationToken);
        ResoniteSceneSlotSnapshot datasetRootSnapshot = await ResoniteSceneSlotSnapshot.CreateAsync(
            client,
            datasetRootSlot,
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
                new ResoniteSlotLocator(completionSourceFileRoot.ID ?? datasetRootSlot.Value),
                completionMeshCode,
                GetSlotPositionOrDefault(completionSourceFileRoot),
                new ResoniteSlotLocator(completionSourceFileRoot.ID ?? datasetRootSlot.Value));
        }

        (Slot Slot, string MeshCode) referenceSourceFileRoot = sourceFileRoots
            .Select(static child => ResoniteSourceMeshCodeAnchor.TryGetConcreteMeshCode(child.Name?.Value ?? string.Empty, out string meshCode)
                ? (Slot: child, MeshCode: meshCode)
                : default)
            .Where(static candidate => candidate.Slot is not null)
            .OrderBy(static candidate => candidate.MeshCode, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Slot.ID ?? string.Empty, StringComparer.Ordinal)
            .FirstOrDefault();
        if (referenceSourceFileRoot.Slot is not null)
        {
            return new SceneAnchor(
                new ResoniteSlotLocator(referenceSourceFileRoot.Slot.ID ?? datasetRootSlot.Value),
                completionMeshCode,
                Add(
                    GetSlotPositionOrDefault(referenceSourceFileRoot.Slot),
                    ComputeMeshCodeOffset(referenceSourceFileRoot.MeshCode, completionMeshCode)),
                new ResoniteSlotLocator(referenceSourceFileRoot.Slot.ID ?? datasetRootSlot.Value));
        }

        return new SceneAnchor(
            datasetRootSlot,
            completionMeshCode,
            GetSlotPositionOrDefault(datasetRootSnapshot.Root ?? throw new InvalidOperationException("Dataset root snapshot did not include a root slot.")),
            ReferenceSourceFileRoot: null);
    }

    private static async Task WaitForSlotAvailableAsync(
        IResoniteLinkClient client,
        ResoniteSlotLocator slot,
        CancellationToken cancellationToken)
    {
        if (string.Equals(slot.Value, RootSlotId, StringComparison.Ordinal))
        {
            return;
        }

        Slot? existingSlot = await client.GetSlotAsync(new ResoniteTransportSlotLocator(slot.Value), 0, cancellationToken);
        if (existingSlot is not null)
        {
            return;
        }

        throw new InvalidOperationException($"ResoniteLink did not surface slot '{slot.Value}' on the initial probe.");
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
        if (!PlateauMeshCode.TryGetGeodeticCenter(referenceMeshCode, out GeodeticCoordinate referenceCenter)
            || !PlateauMeshCode.TryGetGeodeticCenter(meshCode, out GeodeticCoordinate currentCenter))
        {
            return new ResoniteFloat3(0.0, 0.0, 0.0);
        }

        return ComputeOriginOffset(referenceCenter, currentCenter);
    }

    private static ResoniteFloat3 ComputeOriginOffset(
        GeodeticCoordinate referenceCenter,
        GeodeticCoordinate currentCenter)
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
