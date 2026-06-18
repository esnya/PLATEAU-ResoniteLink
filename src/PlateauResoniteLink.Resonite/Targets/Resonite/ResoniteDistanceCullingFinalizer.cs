using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Core.Diagnostics;
using PlateauResoniteLink.Resonite.Targets.Resonite.Execution;
using PlateauResoniteLink.Resonite.Transport.ResoniteLink;

using ResoniteLink;
using PlateauResoniteLink.Core.Application.Importing.Contracts;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal static class ResoniteDistanceCullingFinalizer
{
    public static async Task EmitAsync(
        LiveSendRunState state,
        IResoniteLinkClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(client);

        if (!state.Context.Plan.DistanceCullingEnabled)
        {
            return;
        }

        IReadOnlyList<ResoniteDistanceCullingSourceFilePlan> sourceFilePlans = state.DistanceCulling.CreatePlans();
        if (sourceFilePlans.Count == 0)
        {
            PlateauDiagnostics.Verbose("Distance culling enabled but no eligible LOD parent slots were sent.");
            return;
        }

        foreach (ResoniteDistanceCullingSourceFilePlan sourceFilePlan in sourceFilePlans)
        {
            await EmitSourceFilePlanAsync(
                client,
                sourceFilePlan,
                cancellationToken);
        }
    }

    private static async Task EmitSourceFilePlanAsync(
        IResoniteLinkClient client,
        ResoniteDistanceCullingSourceFilePlan sourceFilePlan,
        CancellationToken cancellationToken)
    {
        Dictionary<DistanceCullingClass, List<string>> targetFieldIdsByClass = new();
        foreach (ResoniteDistanceCullingLodTarget target in sourceFilePlan.Targets)
        {
            string isActiveFieldId = await GetRequiredIsActiveFieldIdAsync(client, target.LodSlot, cancellationToken);
            if (!targetFieldIdsByClass.TryGetValue(target.DistanceCullingClass, out List<string>? targetFieldIds))
            {
                targetFieldIds = [];
                targetFieldIdsByClass[target.DistanceCullingClass] = targetFieldIds;
            }

            targetFieldIds.Add(isActiveFieldId);
        }

        List<DataModelOperation> operations = [];
        foreach ((DistanceCullingClass distanceCullingClass, List<string> targetFieldIds) in targetFieldIdsByClass
            .OrderBy(static pair => pair.Key))
        {
            operations.AddRange(ResoniteDistanceCullingPlanner.CreateOperations(
                sourceFilePlan.SourceFileSlot,
                distanceCullingClass,
                targetFieldIds));
        }

        if (operations.Count == 0)
        {
            return;
        }

        _ = await client.RunDataModelOperationBatchAsync(operations, cancellationToken);
        PlateauDiagnostics.Verbose(
            "Distance culling components emitted for source file root '{SourceFileRoot}' (groups={GroupCount}, operations={OperationCount}).",
            sourceFilePlan.SourceFileSlot.SlotName,
            targetFieldIdsByClass.Count,
            operations.Count);
    }

    private static async Task<string> GetRequiredIsActiveFieldIdAsync(
        IResoniteLinkClient client,
        CreatedSlot lodSlot,
        CancellationToken cancellationToken)
    {
        Slot? slot = await client.GetSlotAsync(
            new ResoniteTransportSlotLocator(lodSlot.Locator.Value),
            depth: 0,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(slot?.IsActive?.ID))
        {
            throw new InvalidOperationException(
                $"LOD parent slot '{lodSlot.SlotName}' ({lodSlot.Locator.Value}) does not expose an addressable IsActive field for distance culling.");
        }

        return slot.IsActive.ID!;
    }
}
