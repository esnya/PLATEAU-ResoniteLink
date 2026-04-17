using System.Security.Cryptography;

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
    string SlotId,
    string MeshCode,
    ResoniteFloat3 Position);

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
        Slot? lastVisibleReferenceMeshRoot = null;
        ResoniteSceneSlotSnapshot datasetRootSnapshot = await ResoniteSceneSlotSnapshot.CreateAsync(
            client,
            datasetRootSlotId,
            1,
            cancellationToken);
        ResoniteSceneChildLookupResult completionRootLookup = datasetRootSnapshot.GetUniqueChildLookupResult(
            completionMeshCode,
            datasetRootSlotId);
        if (completionRootLookup.State == ResoniteSceneChildLookupState.FoundWithId)
        {
            string existingCompletionRootId = completionRootLookup.SlotId!;
            await WaitForSlotAvailableAsync(client, existingCompletionRootId, cancellationToken);
            Slot? completionSlot = await client.GetSlotAsync(existingCompletionRootId, 0, cancellationToken);
            return new SceneAnchor(
                existingCompletionRootId,
                completionMeshCode,
                completionSlot is null ? new ResoniteFloat3(0.0, 0.0, 0.0) : GetSlotPosition(completionSlot));
        }

        Slot? referenceMeshRoot = FindDeterministicReferenceMeshRoot(datasetRootSnapshot);
        if (referenceMeshRoot is not null)
        {
            lastVisibleReferenceMeshRoot = referenceMeshRoot;
        }

        ResoniteFloat3 anchorPosition = lastVisibleReferenceMeshRoot is null
            ? new ResoniteFloat3(0.0, 0.0, 0.0)
            : Add(
                GetSlotPosition(lastVisibleReferenceMeshRoot),
                ComputeMeshCodeOffset(lastVisibleReferenceMeshRoot.Name!.Value, completionMeshCode));

        ResoniteSceneSlotSnapshot finalDatasetRootSnapshot = await ResoniteSceneSlotSnapshot.CreateAsync(
            client,
            datasetRootSlotId,
            1,
            cancellationToken);
        ResoniteSceneChildLookupResult finalCompletionRootLookup = finalDatasetRootSnapshot.GetUniqueChildLookupResult(
            completionMeshCode,
            datasetRootSlotId);
        if (finalCompletionRootLookup.State == ResoniteSceneChildLookupState.FoundWithId)
        {
            string existingAnchorSlotId = finalCompletionRootLookup.SlotId!;
            return new SceneAnchor(existingAnchorSlotId, completionMeshCode, anchorPosition);
        }

        string batchScopeToken = CreateBatchScopeToken();
        PendingBatchSlot pendingAnchorSlot = new(
            $"anchor_slot_local_{batchScopeToken}",
            $"anchor_slot_message_{batchScopeToken}",
            completionMeshCode);
        BatchResponse response = await client.RunDataModelOperationBatchAsync(
            [
                CreateAddSlotOperation(
                    datasetRootSlotId,
                    completionMeshCode,
                    anchorPosition,
                    null,
                    pendingAnchorSlot.LocalId,
                    pendingAnchorSlot.MessageId),
            ],
            cancellationToken);
        return new SceneAnchor(
            ResolveCreatedSlotId(response, pendingAnchorSlot),
            completionMeshCode,
            anchorPosition);
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

    private static Slot? FindDeterministicReferenceMeshRoot(ResoniteSceneSlotSnapshot datasetRootSnapshot)
    {
        return datasetRootSnapshot.Root?.Children?
            .Select(static child => TryGetMeshCodeName(child, out string meshCode)
                ? (Slot: child, MeshCode: meshCode)
                : ((Slot Slot, string MeshCode)?)null)
            .Where(static candidate => candidate.HasValue)
            .Select(static candidate => candidate!.Value)
            .OrderByDescending(static candidate => candidate.MeshCode.Length)
            .ThenBy(static candidate => candidate.MeshCode, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Slot.ID ?? string.Empty, StringComparer.Ordinal)
            .Select(static candidate => candidate.Slot)
            .FirstOrDefault();
    }

    private static bool TryGetMeshCodeName(Slot slot, out string meshCode)
    {
        meshCode = slot.Name?.Value ?? string.Empty;
        return PlateauMeshCode.TryGetCenter(meshCode, out _);
    }

    private static ResoniteFloat3 GetSlotPosition(Slot slot)
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

    private static AddSlot CreateAddSlotOperation(
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        string requestedSlotId,
        string messageId)
    {
        return new AddSlot
        {
            MessageID = messageId,
            Data = new Slot
            {
                ID = requestedSlotId,
                Parent = new Reference
                {
                    TargetID = parentId,
                },
                Name = new Field_string
                {
                    Value = slotName,
                },
                Position = position is null ? null : CreateFloat3(position),
                Rotation = rotation is null ? null : CreateFloatQ(rotation),
            },
        };
    }

    private static string ResolveCreatedSlotId(BatchResponse response, PendingBatchSlot pendingSlot)
    {
        IReadOnlyList<Response> responses = response.Responses
            ?? throw new InvalidOperationException("ResoniteLink batch response did not include per-operation responses.");
        Response operationResponse = responses.SingleOrDefault(
            candidate => string.Equals(candidate.SourceMessageID, pendingSlot.MessageId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"ResoniteLink batch response did not include '{pendingSlot.MessageId}'.");
        ResoniteLinkClient.EnsureSuccess(operationResponse, $"resolve anchor slot '{pendingSlot.SlotName}'");
        return operationResponse is NewEntityId createdEntity && !string.IsNullOrWhiteSpace(createdEntity.EntityId)
            ? createdEntity.EntityId
            : throw new InvalidOperationException($"ResoniteLink batch response did not include an entity ID for '{pendingSlot.SlotName}'.");
    }

    private static Field_float3 CreateFloat3(ResoniteFloat3 value)
    {
        return new Field_float3
        {
            Value = new float3
            {
                x = (float)value.X,
                y = (float)value.Y,
                z = (float)value.Z,
            },
        };
    }

    private static Field_floatQ CreateFloatQ(ResoniteFloatQ value)
    {
        return new Field_floatQ
        {
            Value = new floatQ
            {
                x = (float)value.X,
                y = (float)value.Y,
                z = (float)value.Z,
                w = (float)value.W,
            },
        };
    }

    private readonly record struct PendingBatchSlot(
        string LocalId,
        string MessageId,
        string SlotName);

    private static string CreateBatchScopeToken()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
    }
}
