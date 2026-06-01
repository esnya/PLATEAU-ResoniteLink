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
        ObservedSourceRootSlot[] sourceFileRoots = datasetRootSnapshot.Root?.Children?
            .Where(static child => !string.Equals(child.Name?.Value, "Assets", StringComparison.Ordinal))
            .Select(static child => ObservedSourceRootSlot.TryCreate(child, out ObservedSourceRootSlot sourceRoot)
                ? (SourceRoot: sourceRoot, HasSourceRoot: true)
                : default)
            .Where(static candidate => candidate.HasSourceRoot)
            .Select(static candidate => candidate.SourceRoot)
            .ToArray()
            ?? [];
        ObservedSourceRootSlot completionSourceFileRoot = sourceFileRoots.FirstOrDefault(
            child => child.TryGetConcreteMeshCode(out string meshCode)
                && string.Equals(meshCode, completionMeshCode, StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(completionSourceFileRoot.SlotName))
        {
            return new SceneAnchor(
                new ResoniteSlotLocator(completionSourceFileRoot.SlotId),
                completionMeshCode,
                completionSourceFileRoot.Position,
                new ResoniteSlotLocator(completionSourceFileRoot.SlotId));
        }

        (ObservedSourceRootSlot Slot, string MeshCode) referenceSourceFileRoot = sourceFileRoots
            .Select(static child => child.TryGetConcreteMeshCode(out string meshCode)
                ? (Slot: child, MeshCode: meshCode, HasMeshCode: true)
                : default)
            .Where(static candidate => candidate.HasMeshCode)
            .OrderBy(static candidate => candidate.MeshCode, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Slot.SlotId, StringComparer.Ordinal)
            .Select(static candidate => (candidate.Slot, candidate.MeshCode))
            .FirstOrDefault();
        if (!string.IsNullOrEmpty(referenceSourceFileRoot.Slot.SlotName))
        {
            return new SceneAnchor(
                new ResoniteSlotLocator(referenceSourceFileRoot.Slot.SlotId),
                completionMeshCode,
                Add(
                    referenceSourceFileRoot.Slot.Position,
                    ComputeMeshCodeOffset(referenceSourceFileRoot.MeshCode, completionMeshCode)),
                new ResoniteSlotLocator(referenceSourceFileRoot.Slot.SlotId));
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
