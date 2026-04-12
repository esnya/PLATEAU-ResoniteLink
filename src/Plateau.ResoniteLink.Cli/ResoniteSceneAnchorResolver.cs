using GeographicLib;

using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

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
    private const int VisibilityPollDelayMilliseconds = 50;
    private const int VisibilityPollAttemptLimit = 200;
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
        int attemptLimit = datasetRootExisted ? VisibilityPollAttemptLimit : 1;
        for (int attempt = 1; attempt <= attemptLimit; attempt++)
        {
            ResoniteSceneSlotSnapshot datasetRootSnapshot = await ResoniteSceneSlotSnapshot.CreateAsync(
                client,
                datasetRootSlotId,
                1,
                cancellationToken);
            Slot? existingCompletionRoot = datasetRootSnapshot.TryGetUniqueChildByName(completionMeshCode, datasetRootSlotId);
            string? existingCompletionRootId = existingCompletionRoot?.ID;
            if (existingCompletionRootId is not null)
            {
                await WaitForSlotAvailableAsync(client, existingCompletionRootId, cancellationToken);
                Slot? completionSlot = await client.GetSlotAsync(existingCompletionRootId, 0, cancellationToken);
                return new SceneAnchor(
                    existingCompletionRootId,
                    completionMeshCode,
                    completionSlot is null ? new ResoniteFloat3(0.0, 0.0, 0.0) : GetSlotPosition(completionSlot));
            }

            Slot? referenceMeshRoot = datasetRootSnapshot.Root?.Children?
                .FirstOrDefault(static child => TryGetMeshCodeName(child, out _));
            if (referenceMeshRoot is not null)
            {
                lastVisibleReferenceMeshRoot = referenceMeshRoot;
            }

            if (!datasetRootExisted)
            {
                break;
            }

            if (attempt < attemptLimit)
            {
                await Task.Delay(VisibilityPollDelayMilliseconds, cancellationToken);
            }
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
        string? existingAnchorSlotId = finalDatasetRootSnapshot
            .TryGetUniqueChildByName(completionMeshCode, datasetRootSlotId)?
            .ID;
        if (existingAnchorSlotId is not null)
        {
            return new SceneAnchor(existingAnchorSlotId, completionMeshCode, anchorPosition);
        }

        string createdAnchorId = await client.AddSlotAsync(
            CreateAddSlotOperation(
                datasetRootSlotId,
                completionMeshCode,
                anchorPosition,
                null),
            cancellationToken);
        return new SceneAnchor(createdAnchorId, completionMeshCode, anchorPosition);
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

        for (int attempt = 1; attempt <= VisibilityPollAttemptLimit; attempt++)
        {
            Slot? slot = await client.GetSlotAsync(slotId, 0, cancellationToken);
            if (slot is not null)
            {
                return;
            }

            if (attempt < VisibilityPollAttemptLimit)
            {
                await Task.Delay(VisibilityPollDelayMilliseconds, cancellationToken);
            }
        }

        throw new InvalidOperationException($"ResoniteLink did not surface slot '{slotId}'.");
    }

    private static string? TryFindUniqueChildSlotIdByName(
        Slot? parentSlot,
        string slotName)
    {
        if (parentSlot?.Children is null)
        {
            return null;
        }

        Slot[] matches = parentSlot.Children
            .Where(child => string.Equals(child.Name?.Value, slotName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        if (matches.Length > 1)
        {
            string parentIdentifier = parentSlot.ID ?? "<unknown>";
            throw new InvalidOperationException(
                $"Parent slot '{parentIdentifier}' contains multiple child slots named '{slotName}'.");
        }

        return matches[0].ID
            ?? throw new InvalidOperationException(
                $"Child slot '{slotName}' under parent '{parentSlot.ID ?? "<unknown>"}' did not surface an ID.");
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
        ResoniteFloatQ? rotation)
    {
        return new AddSlot
        {
            Data = new Slot
            {
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
}
