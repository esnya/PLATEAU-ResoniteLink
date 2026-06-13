using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Application.Importing;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal static class ResoniteDistanceCullingPlanner
{
    internal const string DisabledVariableName = "PLATEAU.DistanceCulling.Disabled";

    private const string DynamicValueVariableDriverFloatComponentType =
        "[FrooxEngine]FrooxEngine.DynamicValueVariableDriver<float>";
    private const string DynamicValueVariableDriverBoolComponentType =
        "[FrooxEngine]FrooxEngine.DynamicValueVariableDriver<bool>";
    private const string UserDistanceValueDriverBoolComponentType =
        "[FrooxEngine]FrooxEngine.UserDistanceValueDriver<bool>";
    private const string ValueMultiDriverBoolComponentType =
        "[FrooxEngine]FrooxEngine.ValueMultiDriver<bool>";

    public static IReadOnlyList<DataModelOperation> CreateOperations(
        CreatedSlot sourceFileSlot,
        DistanceCullingClass distanceCullingClass,
        IReadOnlyList<string> targetIsActiveFieldIds)
    {
        ArgumentNullException.ThrowIfNull(targetIsActiveFieldIds);
        if (targetIsActiveFieldIds.Count == 0)
        {
            return [];
        }

        ResoniteDistanceCullingGate gate = CreateGate(distanceCullingClass);
        ResoniteBatchOperations.BatchActionBuilder batchBuilder = new();
        string cullingTargetId = CreateDistanceCullingTarget(
            batchBuilder,
            sourceFileSlot,
            targetIsActiveFieldIds);
        ResoniteBatchOperations.BatchTemporaryFieldId distanceFieldId = batchBuilder.AllocateFieldId();
        ResoniteBatchOperations.BatchTemporaryFieldId farValueFieldId = batchBuilder.AllocateFieldId();

        batchBuilder.AddComponent(
            sourceFileSlot.Locator.Value,
            UserDistanceValueDriverBoolComponentType,
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Node"] = new Field_Enum
                {
                    Value = "View",
                },
                ["Distance"] = new Field_float
                {
                    ID = distanceFieldId.Value,
                    Value = gate.DistanceMeters,
                },
                ["TargetField"] = new Reference
                {
                    TargetID = cullingTargetId,
                },
                ["NearValue"] = new Field_bool
                {
                    Value = true,
                },
                ["FarValue"] = new Field_bool
                {
                    ID = farValueFieldId.Value,
                    Value = false,
                },
            });
        batchBuilder.AddComponent(
            sourceFileSlot.Locator.Value,
            DynamicValueVariableDriverFloatComponentType,
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["VariableName"] = new Field_string
                {
                    Value = gate.DistanceVariableName,
                },
                ["Target"] = new Reference
                {
                    TargetID = distanceFieldId.Value,
                },
                ["DefaultValue"] = new Field_float
                {
                    Value = gate.DistanceMeters,
                },
            });
        batchBuilder.AddComponent(
            sourceFileSlot.Locator.Value,
            DynamicValueVariableDriverBoolComponentType,
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["VariableName"] = new Field_string
                {
                    Value = DisabledVariableName,
                },
                ["Target"] = new Reference
                {
                    TargetID = farValueFieldId.Value,
                },
                ["DefaultValue"] = new Field_bool
                {
                    Value = false,
                },
            });
        return batchBuilder.Actions;
    }

    private static string CreateDistanceCullingTarget(
        ResoniteBatchOperations.BatchActionBuilder batchBuilder,
        CreatedSlot sourceFileSlot,
        IReadOnlyList<string> targetIsActiveFieldIds)
    {
        if (targetIsActiveFieldIds.Count == 1)
        {
            return targetIsActiveFieldIds[0];
        }

        ResoniteBatchOperations.BatchTemporaryFieldId multiDriverValueFieldId = batchBuilder.AllocateFieldId();
        batchBuilder.AddComponent(
            sourceFileSlot.Locator.Value,
            ValueMultiDriverBoolComponentType,
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Value"] = new Field_bool
                {
                    ID = multiDriverValueFieldId.Value,
                    Value = true,
                },
                ["Drives"] = new SyncList
                {
                    Elements = targetIsActiveFieldIds
                        .Select(static targetFieldId => (Member)new Reference
                        {
                            TargetID = targetFieldId,
                        })
                        .ToList(),
                },
            });
        return multiDriverValueFieldId.Value;
    }

    private static ResoniteDistanceCullingGate CreateGate(DistanceCullingClass distanceCullingClass)
    {
        return distanceCullingClass switch
        {
            DistanceCullingClass.Building => Create("BLDG", 5500.0f),
            DistanceCullingClass.Landmark => Create("Landmark", 10500.0f),
            DistanceCullingClass.FurnitureLod2 => Create("FRN.LOD2", 5500.0f),
            DistanceCullingClass.FurnitureLod3 => Create("FRN.LOD3", 1500.0f),
            DistanceCullingClass.BridgeLod2 => Create("BRID.LOD2", 4500.0f),
            DistanceCullingClass.TransportationLod3 => Create("TRAN.LOD3", 2500.0f),
            DistanceCullingClass.VegetationLod2 => Create("VEG.LOD2", 2500.0f),
            DistanceCullingClass.VegetationLod3 => Create("VEG.LOD3", 2500.0f),
            _ => throw new ArgumentOutOfRangeException(nameof(distanceCullingClass), distanceCullingClass, "Unsupported distance culling class."),
        };
    }

    private static ResoniteDistanceCullingGate Create(string variableNameStem, float distanceMeters)
    {
        return new ResoniteDistanceCullingGate(
            $"PLATEAU.DistanceCulling.{variableNameStem}.Distance",
            distanceMeters);
    }
}

internal sealed record ResoniteDistanceCullingGate(
    string DistanceVariableName,
    float DistanceMeters);
