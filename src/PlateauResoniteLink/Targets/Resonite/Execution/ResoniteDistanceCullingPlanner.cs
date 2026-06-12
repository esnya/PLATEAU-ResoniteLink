using System;
using System.Collections.Generic;
using System.Linq;

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

    public static void AddComponents(
        List<PlannedBatchComponentEmission> componentEmissions,
        CreatedSlot sourceFileSlot,
        ResoniteDistanceCullingGate gate,
        IReadOnlyList<PlannedFieldReference> targetIsActiveFields)
    {
        ArgumentNullException.ThrowIfNull(componentEmissions);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(targetIsActiveFields);
        if (targetIsActiveFields.Count == 0)
        {
            return;
        }

        PlannedSlotTargetReference sourceFileRootTarget = PlannedSlotTargetReference.CanonicalSlot(sourceFileSlot.Locator);
        PlannedWorldElementReference cullingTarget = CreateDistanceCullingTarget(
            componentEmissions,
            sourceFileRootTarget,
            targetIsActiveFields);
        PlannedDriverTargetBundle distanceDriverTarget = PlannedDriverTargetBundle.Create(new Field_float
        {
            Value = gate.DistanceMeters,
        });
        PlannedDriverTargetBundle farValueDriverTarget = PlannedDriverTargetBundle.Create(new Field_bool
        {
            Value = false,
        });

        componentEmissions.Add(new PlannedBatchComponentEmission(
            sourceFileRootTarget,
            UserDistanceValueDriverBoolComponentType,
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["Node"] = PlannedMembers.Literal(new Field_Enum
                {
                    Value = "View",
                }),
                ["Distance"] = distanceDriverTarget.Field,
                ["TargetField"] = PlannedMembers.Reference(cullingTarget),
                ["NearValue"] = PlannedMembers.Literal(new Field_bool
                {
                    Value = true,
                }),
                ["FarValue"] = farValueDriverTarget.Field,
            }));
        componentEmissions.Add(new PlannedBatchComponentEmission(
            sourceFileRootTarget,
            DynamicValueVariableDriverFloatComponentType,
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["VariableName"] = PlannedMembers.Literal(new Field_string
                {
                    Value = gate.DistanceVariableName,
                }),
                ["Target"] = distanceDriverTarget.Target,
                ["DefaultValue"] = distanceDriverTarget.DefaultValue,
            }));
        componentEmissions.Add(new PlannedBatchComponentEmission(
            sourceFileRootTarget,
            DynamicValueVariableDriverBoolComponentType,
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["VariableName"] = PlannedMembers.Literal(new Field_string
                {
                    Value = DisabledVariableName,
                }),
                ["Target"] = farValueDriverTarget.Target,
                ["DefaultValue"] = farValueDriverTarget.DefaultValue,
            }));
    }

    public static ResoniteDistanceCullingGate? TryCreateGate(ResoniteConstructionCityObject cityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        if (string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string packageName = cityObject.PackageName.ToUpperInvariant();
        return packageName switch
        {
            "BLDG" or "UBLD" when cityObject.Landmark => Create("Landmark", 10500.0f),
            "BLDG" or "UBLD" => Create("BLDG", 5500.0f),
            "FRN" when cityObject.LodLevel == 2 => Create("FRN.LOD2", 5500.0f),
            "FRN" when cityObject.LodLevel == 3 => Create("FRN.LOD3", 1500.0f),
            "BRID" when cityObject.LodLevel == 2 => Create("BRID.LOD2", 4500.0f),
            "TRAN" when cityObject.LodLevel == 3 => Create("TRAN.LOD3", 2500.0f),
            "VEG" when cityObject.LodLevel == 2 => Create("VEG.LOD2", 2500.0f),
            "VEG" when cityObject.LodLevel == 3 => Create("VEG.LOD3", 2500.0f),
            _ => null,
        };
    }

    private static PlannedWorldElementReference CreateDistanceCullingTarget(
        List<PlannedBatchComponentEmission> componentEmissions,
        PlannedSlotTargetReference sourceFileRootTarget,
        IReadOnlyList<PlannedFieldReference> targetIsActiveFields)
    {
        if (targetIsActiveFields.Count == 1)
        {
            return PlannedWorldElementReference.Planned(targetIsActiveFields[0]);
        }

        PlannedDriverTargetBundle multiDriverValue = PlannedDriverTargetBundle.Create(new Field_bool
        {
            Value = true,
        });
        componentEmissions.Add(new PlannedBatchComponentEmission(
            sourceFileRootTarget,
            ValueMultiDriverBoolComponentType,
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["Value"] = multiDriverValue.Field,
                ["Drives"] = new PlannedSyncListMember(
                    targetIsActiveFields
                        .Select(static target => PlannedMembers.Reference(PlannedWorldElementReference.Planned(target)))
                        .ToArray()),
            }));
        return PlannedWorldElementReference.Planned(multiDriverValue.Field.Field);
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
