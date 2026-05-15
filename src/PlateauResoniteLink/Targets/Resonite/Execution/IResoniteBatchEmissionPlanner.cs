using System;
using System.Collections.Generic;
using System.Linq;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal interface IResoniteBatchEmissionPlanner
{
    PlannedBatchEmission Create(
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots,
        PlannedSceneObjectEmission emissionPlan);
}

internal sealed class ResoniteBatchEmissionPlanner : IResoniteBatchEmissionPlanner
{
    private const float DefaultNormalScale = 1.0f;
    private const float DefaultBundledHeightScale = 0.002f;
    private const string TerrainGridDetailDynamicVariableName = "PLATEAU.Terrain.Grid.Detail";
    private const string TerrainStaticEnabledVariableName = "PLATEAU.Terrain.Static.Enabled";
    private const string DynamicValueVariableDriverFloatComponentType =
        "[FrooxEngine]FrooxEngine.DynamicValueVariableDriver<float>";
    private const string DynamicValueVariableDriverBoolComponentType =
        "[FrooxEngine]FrooxEngine.DynamicValueVariableDriver<bool>";
    private const string ValueGradientDriverInt2ComponentType =
        "[FrooxEngine]FrooxEngine.ValueGradientDriver<int2>";
    private const string BooleanAssetDriverMeshComponentType =
        "[FrooxEngine]FrooxEngine.BooleanAssetDriver<[FrooxEngine]FrooxEngine.Mesh>";

    public PlannedBatchEmission Create(
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots,
        PlannedSceneObjectEmission emissionPlan)
    {
        ArgumentNullException.ThrowIfNull(objectSlots);
        ArgumentNullException.ThrowIfNull(emissionPlan);

        List<PlannedBatchSlotEmission> slotEmissions = [];
        List<PlannedBatchComponentEmission> componentEmissions = [];
        List<BatchPlanSlotLocator> slotResolutionTargets = [];
        List<BatchPlanComponentLocator> componentResolutionTargets = [];
        int nextSlotLocator = 0;
        int nextComponentLocator = 0;
        int nextFieldLocator = 0;

        BatchPlanSlotLocator meshAssetSlotId = CreateBatchPlanSlotLocator(ref nextSlotLocator);
        slotEmissions.Add(new PlannedBatchSlotEmission(
            meshAssetSlotId,
            PlannedSlotTargetReference.CanonicalSlot(objectSlots.AssetLodSlot.Locator),
            emissionPlan.GeometryAsset.MeshAssetSlotName,
            null,
            null));
        slotResolutionTargets.Add(meshAssetSlotId);

        BatchPlanComponentLocator rendererGeometryComponentId = CreateBatchPlanComponentLocator(ref nextComponentLocator);
        PlannedTerrainGridMeshBundle? terrainGridMesh = null;
        PlannedDynamicTerrainMeshBundle? dynamicTerrainMesh = null;
        switch (emissionPlan.GeometryAsset)
        {
            case PlannedTriangleMeshGeometryAsset triangleMesh:
                componentEmissions.Add(new PlannedBatchComponentEmission(
                    rendererGeometryComponentId,
                    PlannedSlotTargetReference.PlannedSlot(meshAssetSlotId),
                    "[FrooxEngine]FrooxEngine.StaticMesh",
                    new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
                    {
                        ["URL"] = PlannedMembers.Literal(new Field_Uri
                        {
                            Value = triangleMesh.MeshUri,
                        }),
                    }));
                break;
            case PlannedTerrainGridGeometryAsset heightMap:
                terrainGridMesh = AddPlannedTerrainGridTextureAndCreateGridBundle(
                    rendererGeometryComponentId,
                    slotEmissions,
                    componentEmissions,
                    slotResolutionTargets,
                    objectSlots,
                    heightMap.TerrainGridAssetSlotName,
                    heightMap.Geometry,
                    heightMap.HeightTextureUri,
                    heightMap.UvScale,
                    heightMap.UvOffset,
                    ref nextSlotLocator,
                    ref nextComponentLocator,
                    ref nextFieldLocator);
                break;
            case PlannedDynamicTerrainGeometryAsset dynamicTerrain:
                componentEmissions.Add(new PlannedBatchComponentEmission(
                    rendererGeometryComponentId,
                    PlannedSlotTargetReference.PlannedSlot(meshAssetSlotId),
                    "[FrooxEngine]FrooxEngine.StaticMesh",
                    new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
                    {
                        ["URL"] = PlannedMembers.Literal(new Field_Uri
                        {
                            Value = dynamicTerrain.StaticMeshUri,
                        }),
                    }));
                PlannedWorldElementReference dynamicStaticMeshTarget = PlannedWorldElementReference.Planned(rendererGeometryComponentId);
                BatchPlanComponentLocator gridMeshComponentId = CreateBatchPlanComponentLocator(ref nextComponentLocator);
                terrainGridMesh = AddPlannedTerrainGridTextureAndCreateGridBundle(
                    gridMeshComponentId,
                    slotEmissions,
                    componentEmissions,
                    slotResolutionTargets,
                    objectSlots,
                    dynamicTerrain.TerrainGridAssetSlotName,
                    dynamicTerrain.GridGeometry,
                    dynamicTerrain.HeightTextureUri,
                    dynamicTerrain.UvScale,
                    dynamicTerrain.UvOffset,
                    ref nextSlotLocator,
                    ref nextComponentLocator,
                    ref nextFieldLocator);
                dynamicTerrainMesh = new PlannedDynamicTerrainMeshBundle(
                    PlannedWorldElementReference.Planned(gridMeshComponentId),
                    dynamicStaticMeshTarget);
                rendererGeometryComponentId = terrainGridMesh.ComponentIdentity;
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported planned geometry asset type '{emissionPlan.GeometryAsset.GetType().Name}'.");
        }
        componentResolutionTargets.Add(rendererGeometryComponentId);

        Dictionary<PlannedMaterialAsset, PlannedWorldElementReference> emittedMaterialTargets =
            new(ReferenceEqualityComparer.Instance);
        foreach (PlannedMaterialAsset materialAsset in emissionPlan.MaterialAssets)
        {
            switch (materialAsset)
            {
                case PlannedReusableMaterialAsset reusableMaterial:
                    emittedMaterialTargets[reusableMaterial] = PlannedWorldElementReference.Canonical(reusableMaterial.Target);
                    break;
                case PlannedDedicatedMaterialAsset dedicatedMaterial:
                    PlannedWorldElementReference emittedMaterialTarget = AddPlannedDedicatedMaterialEmissions(
                        slotEmissions,
                        componentEmissions,
                        PlannedSlotTargetReference.PlannedSlot(meshAssetSlotId),
                        dedicatedMaterial,
                        ref nextSlotLocator,
                        ref nextComponentLocator);
                    emittedMaterialTargets[dedicatedMaterial] = emittedMaterialTarget;
                    break;
                default:
                    throw new InvalidOperationException(
                    $"Unsupported planned material asset type '{materialAsset.GetType().Name}'.");
            }
        }

        BatchPlanSlotLocator presentationSlotId = CreateBatchPlanSlotLocator(ref nextSlotLocator);
        slotEmissions.Add(new PlannedBatchSlotEmission(
            presentationSlotId,
            PlannedSlotTargetReference.CanonicalSlot(objectSlots.LodSlot.Locator),
            objectSlots.CityObjectSlotName,
            objectSlots.CityObjectLocalPosition,
            objectSlots.CityObjectRotation));
        slotResolutionTargets.Add(presentationSlotId);

        if (terrainGridMesh is not null)
        {
            componentEmissions.Add(new PlannedBatchComponentEmission(
                terrainGridMesh.ComponentIdentity,
                PlannedSlotTargetReference.PlannedSlot(presentationSlotId),
                "[FrooxEngine]FrooxEngine.GridMesh",
                CreateTerrainGridMeshMembers(terrainGridMesh)));
            AddTerrainGridPointsDriverComponents(
                componentEmissions,
                presentationSlotId,
                terrainGridMesh,
                ref nextComponentLocator,
                ref nextFieldLocator);
        }

        BatchPlanFieldLocator rendererMeshFieldId = CreateBatchPlanFieldLocator(ref nextFieldLocator);
        componentEmissions.Add(new PlannedBatchComponentEmission(
            CreateBatchPlanComponentLocator(ref nextComponentLocator),
            PlannedSlotTargetReference.PlannedSlot(presentationSlotId),
            "[FrooxEngine]FrooxEngine.MeshRenderer",
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["Mesh"] = PlannedMembers.AddressableReference(
                    rendererMeshFieldId,
                    ResolveInitialMeshTarget(dynamicTerrainMesh, rendererGeometryComponentId)),
                ["Materials"] = CreateRendererMaterials(emissionPlan.Renderer.MaterialBindings, emittedMaterialTargets),
                ["MaterialPropertyBlocks"] = CreateRendererMaterialPropertyBlocks(
                    componentEmissions,
                    meshAssetSlotId,
                    presentationSlotId,
                    emissionPlan.Renderer.MaterialBindings,
                    ref nextComponentLocator),
            }));
        if (dynamicTerrainMesh is not null)
        {
            AddDynamicMeshSwitchComponents(
                componentEmissions,
                presentationSlotId,
                rendererMeshFieldId,
                dynamicTerrainMesh,
                ref nextComponentLocator,
                ref nextFieldLocator);
        }

        BatchPlanFieldLocator colliderMeshFieldId = CreateBatchPlanFieldLocator(ref nextFieldLocator);
        componentEmissions.Add(new PlannedBatchComponentEmission(
            CreateBatchPlanComponentLocator(ref nextComponentLocator),
            PlannedSlotTargetReference.PlannedSlot(presentationSlotId),
            "[FrooxEngine]FrooxEngine.MeshCollider",
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["Type"] = PlannedMembers.Literal(new Field_Enum
                {
                    Value = emissionPlan.Collider.CollisionEnabled ? "Static" : "NoCollision",
                }),
                ["CharacterCollider"] = PlannedMembers.Literal(new Field_bool
                {
                    Value = emissionPlan.Collider.CollisionEnabled,
                }),
                ["Mesh"] = PlannedMembers.AddressableReference(
                    colliderMeshFieldId,
                    ResolveInitialMeshTarget(dynamicTerrainMesh, rendererGeometryComponentId)),
            }));
        if (dynamicTerrainMesh is not null)
        {
            AddDynamicMeshSwitchComponents(
                componentEmissions,
                presentationSlotId,
                colliderMeshFieldId,
                dynamicTerrainMesh,
                ref nextComponentLocator,
                ref nextFieldLocator);
        }

        return new PlannedBatchEmission(
            slotEmissions,
            componentEmissions,
            slotResolutionTargets,
            componentResolutionTargets);
    }

    private static PlannedWorldElementReference ResolveInitialMeshTarget(
        PlannedDynamicTerrainMeshBundle? dynamicTerrainMesh,
        BatchPlanComponentLocator defaultGeometryComponentId)
    {
        return dynamicTerrainMesh?.InitialMeshTarget ?? PlannedWorldElementReference.Planned(defaultGeometryComponentId);
    }

    private static PlannedTerrainGridMeshBundle AddPlannedTerrainGridTextureAndCreateGridBundle(
        BatchPlanComponentLocator gridMeshComponentId,
        List<PlannedBatchSlotEmission> slotEmissions,
        List<PlannedBatchComponentEmission> componentEmissions,
        List<BatchPlanSlotLocator> slotResolutionTargets,
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots,
        string terrainGridAssetSlotName,
        ResoniteTerrainGridGeometry geometry,
        Uri heightTextureUri,
        ResoniteFloat2? uvScale,
        ResoniteFloat2? uvOffset,
        ref int nextSlotLocator,
        ref int nextComponentLocator,
        ref int nextFieldLocator)
    {
        BatchPlanSlotLocator heightMapAssetSlotId = CreateBatchPlanSlotLocator(ref nextSlotLocator);
        BatchPlanComponentLocator heightTextureComponentId = CreateBatchPlanComponentLocator(ref nextComponentLocator);
        slotEmissions.Add(new PlannedBatchSlotEmission(
            heightMapAssetSlotId,
            PlannedSlotTargetReference.CanonicalSlot(objectSlots.AssetLodSlot.Locator),
            terrainGridAssetSlotName,
            null,
            null));
        slotResolutionTargets.Add(heightMapAssetSlotId);
        componentEmissions.Add(new PlannedBatchComponentEmission(
            heightTextureComponentId,
            PlannedSlotTargetReference.PlannedSlot(heightMapAssetSlotId),
            "[FrooxEngine]FrooxEngine.StaticTexture2D",
            ResoniteGeometryAssetAssembler.CreateTerrainGridTextureMembers(heightTextureUri)
                .ToDictionary(static pair => pair.Key, static pair => PlannedMembers.Literal(pair.Value), StringComparer.Ordinal)));

        double displacementMagnitude = Math.Max(geometry.MaxHeight - geometry.MinHeight, 0.0);
        BatchPlanFieldLocator pointsFieldId = CreateBatchPlanFieldLocator(ref nextFieldLocator);
        Field_int2 points = new()
        {
            Value = new int2
            {
                x = geometry.Width,
                y = geometry.Height,
            },
        };
        Field_float2 size = new()
        {
            Value = new float2
            {
                x = (float)geometry.Size.X,
                y = (float)geometry.Size.Y,
            },
        };
        Field_float displacement = new()
        {
            Value = (float)displacementMagnitude,
        };
        Field_float2? plannedUvScale = uvScale is null
            ? null
            : new Field_float2
            {
                Value = new float2
                {
                    x = (float)uvScale.X,
                    y = (float)uvScale.Y,
                },
            };

        Field_float2? plannedUvOffset = uvOffset is null
            ? null
            : new Field_float2
            {
                Value = new float2
                {
                    x = (float)uvOffset.X,
                    y = (float)uvOffset.Y,
                },
            };

        return new PlannedTerrainGridMeshBundle(
            gridMeshComponentId,
            pointsFieldId,
            points,
            size,
            displacement,
            PlannedWorldElementReference.Planned(heightTextureComponentId),
            plannedUvScale,
            plannedUvOffset);
    }

    private static Dictionary<string, PlannedMember> CreateTerrainGridMeshMembers(
        PlannedTerrainGridMeshBundle terrainGridMesh)
    {
        Dictionary<string, PlannedMember> members = new(StringComparer.Ordinal)
        {
            ["Points"] = PlannedMembers.AddressableField(terrainGridMesh.PointsIdentity, terrainGridMesh.Points),
            ["Size"] = PlannedMembers.Literal(terrainGridMesh.Size),
            ["DisplacementMagnitude"] = PlannedMembers.Literal(terrainGridMesh.DisplacementMagnitude),
            ["DisplacementTexture"] = PlannedMembers.Reference(terrainGridMesh.DisplacementTexture),
        };
        if (terrainGridMesh.UvScale is not null)
        {
            members["UVScale"] = PlannedMembers.Literal(terrainGridMesh.UvScale);
        }

        if (terrainGridMesh.UvOffset is not null)
        {
            members["UVOffset"] = PlannedMembers.Literal(terrainGridMesh.UvOffset);
        }

        return members;
    }

    private static void AddDynamicMeshSwitchComponents(
        List<PlannedBatchComponentEmission> componentEmissions,
        BatchPlanSlotLocator presentationSlotId,
        BatchPlanFieldLocator targetMeshFieldId,
        PlannedDynamicTerrainMeshBundle dynamicTerrainMesh,
        ref int nextComponentLocator,
        ref int nextFieldLocator)
    {
        BatchPlanFieldLocator stateFieldId = CreateBatchPlanFieldLocator(ref nextFieldLocator);
        PlannedDriverTargetBundle stateDriverTarget = PlannedDriverTargetBundle.Create(
            stateFieldId,
            new Field_bool
            {
                Value = false,
            });
        Dictionary<string, PlannedMember> switchMembers = new(StringComparer.Ordinal)
        {
            ["State"] = stateDriverTarget.Field,
            ["Target"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(targetMeshFieldId)),
            ["FalseTarget"] = PlannedMembers.Reference(dynamicTerrainMesh.GridMeshTarget),
            ["TrueTarget"] = PlannedMembers.Reference(dynamicTerrainMesh.StaticMeshTarget),
        };
        componentEmissions.Add(new PlannedBatchComponentEmission(
            CreateBatchPlanComponentLocator(ref nextComponentLocator),
            PlannedSlotTargetReference.PlannedSlot(presentationSlotId),
            BooleanAssetDriverMeshComponentType,
            switchMembers));
        componentEmissions.Add(new PlannedBatchComponentEmission(
            CreateBatchPlanComponentLocator(ref nextComponentLocator),
            PlannedSlotTargetReference.PlannedSlot(presentationSlotId),
            DynamicValueVariableDriverBoolComponentType,
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["VariableName"] = PlannedMembers.Literal(new Field_string
                {
                    Value = TerrainStaticEnabledVariableName,
                }),
                ["Target"] = stateDriverTarget.Target,
                ["DefaultValue"] = stateDriverTarget.DefaultValue,
            }));
    }

    private static void AddTerrainGridPointsDriverComponents(
        List<PlannedBatchComponentEmission> componentEmissions,
        BatchPlanSlotLocator presentationSlotId,
        PlannedTerrainGridMeshBundle terrainGridMesh,
        ref int nextComponentLocator,
        ref int nextFieldLocator)
    {
        BatchPlanFieldLocator progressFieldId = CreateBatchPlanFieldLocator(ref nextFieldLocator);
        PlannedDriverTargetBundle progressDriverTarget = PlannedDriverTargetBundle.Create(
            progressFieldId,
            new Field_float
            {
                Value = 1.0f,
            });
        componentEmissions.Add(new PlannedBatchComponentEmission(
            CreateBatchPlanComponentLocator(ref nextComponentLocator),
            PlannedSlotTargetReference.PlannedSlot(presentationSlotId),
            ValueGradientDriverInt2ComponentType,
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["Progress"] = progressDriverTarget.Field,
                ["Target"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(terrainGridMesh.PointsIdentity)),
                ["Interpolate"] = PlannedMembers.Literal(new Field_bool
                {
                    Value = true,
                }),
                ["Points"] = PlannedMembers.List(
                    PlannedMembers.Literal(CreateTerrainGridPointsGradientPoint(0.0f, new int2
                    {
                        x = 2,
                        y = 2,
                    })),
                    PlannedMembers.Literal(CreateTerrainGridPointsGradientPoint(1.0f, terrainGridMesh.Points.Value))),
            }));
        componentEmissions.Add(new PlannedBatchComponentEmission(
            CreateBatchPlanComponentLocator(ref nextComponentLocator),
            PlannedSlotTargetReference.PlannedSlot(presentationSlotId),
            DynamicValueVariableDriverFloatComponentType,
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["VariableName"] = PlannedMembers.Literal(new Field_string
                {
                    Value = TerrainGridDetailDynamicVariableName,
                }),
                ["Target"] = progressDriverTarget.Target,
                ["DefaultValue"] = progressDriverTarget.DefaultValue,
            }));
    }

    private static SyncObject CreateTerrainGridPointsGradientPoint(float position, int2 value)
    {
        return new SyncObject
        {
            Members = new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Position"] = new Field_float
                {
                    Value = position,
                },
                ["Value"] = new Field_int2
                {
                    Value = value,
                },
            },
        };
    }

    private static PlannedWorldElementReference AddPlannedDedicatedMaterialEmissions(
        List<PlannedBatchSlotEmission> slotEmissions,
        List<PlannedBatchComponentEmission> componentEmissions,
        PlannedSlotTargetReference meshAssetSlotTarget,
        PlannedDedicatedMaterialAsset plannedMaterial,
        ref int nextSlotLocator,
        ref int nextComponentLocator)
    {
        ResoniteMaterialBinding material = plannedMaterial.Material;
        PlannedSlotTargetReference materialContainerTarget = meshAssetSlotTarget;
        if (plannedMaterial.PreserveDedicatedMaterialSlot)
        {
            BatchPlanSlotLocator materialSlotId = CreateBatchPlanSlotLocator(ref nextSlotLocator);
            string materialSlotName = plannedMaterial.DedicatedMaterialSlotName
                ?? throw new InvalidOperationException("Dedicated material slot preservation requires a planned ordinal slot name.");
            slotEmissions.Add(new PlannedBatchSlotEmission(
                materialSlotId,
                meshAssetSlotTarget,
                materialSlotName,
                null,
                null));
            materialContainerTarget = PlannedSlotTargetReference.PlannedSlot(materialSlotId);
        }

        Dictionary<string, PlannedMember> materialMembers = ResoniteMaterialComponentPolicy.CreateMembers(material)
            .ToDictionary(static pair => pair.Key, static pair => PlannedMembers.Literal(pair.Value), StringComparer.Ordinal);

        Uri? albedoTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(
            plannedMaterial.Textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Albedo);
        if (albedoTextureUri is not null)
        {
            BatchPlanComponentLocator albedoTextureId = CreateBatchPlanComponentLocator(ref nextComponentLocator);
            componentEmissions.Add(new PlannedBatchComponentEmission(
                albedoTextureId,
                materialContainerTarget,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    albedoTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Albedo)
                    .ToDictionary(static pair => pair.Key, static pair => PlannedMembers.Literal(pair.Value), StringComparer.Ordinal)));
            materialMembers["AlbedoTexture"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(albedoTextureId));
        }

        Uri? normalTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(
            plannedMaterial.Textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Normal);
        if (normalTextureUri is not null)
        {
            BatchPlanComponentLocator normalTextureId = CreateBatchPlanComponentLocator(ref nextComponentLocator);
            componentEmissions.Add(new PlannedBatchComponentEmission(
                normalTextureId,
                materialContainerTarget,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    normalTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Normal)
                    .ToDictionary(static pair => pair.Key, static pair => PlannedMembers.Literal(pair.Value), StringComparer.Ordinal)));
            materialMembers["NormalMap"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(normalTextureId));
            materialMembers["NormalScale"] = PlannedMembers.Literal(new Field_float
            {
                Value = DefaultNormalScale,
            });
        }

        Uri? heightTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(
            plannedMaterial.Textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Height);
        if (heightTextureUri is not null)
        {
            BatchPlanComponentLocator heightTextureId = CreateBatchPlanComponentLocator(ref nextComponentLocator);
            componentEmissions.Add(new PlannedBatchComponentEmission(
                heightTextureId,
                materialContainerTarget,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    heightTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Height)
                    .ToDictionary(static pair => pair.Key, static pair => PlannedMembers.Literal(pair.Value), StringComparer.Ordinal)));
            materialMembers["HeightMap"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(heightTextureId));
            materialMembers["HeightScale"] = PlannedMembers.Literal(new Field_float
            {
                Value = DefaultBundledHeightScale,
            });
        }

        Uri? metallicTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(
            plannedMaterial.Textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Metallic);
        if (metallicTextureUri is not null)
        {
            BatchPlanComponentLocator metallicTextureId = CreateBatchPlanComponentLocator(ref nextComponentLocator);
            componentEmissions.Add(new PlannedBatchComponentEmission(
                metallicTextureId,
                materialContainerTarget,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    metallicTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Metallic)
                    .ToDictionary(static pair => pair.Key, static pair => PlannedMembers.Literal(pair.Value), StringComparer.Ordinal)));
            materialMembers["MetallicMap"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(metallicTextureId));
            materialMembers["OcclusionMap"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(metallicTextureId));
        }

        Uri? emissionTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(
            plannedMaterial.Textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Emission);
        if (emissionTextureUri is not null)
        {
            BatchPlanComponentLocator emissionTextureId = CreateBatchPlanComponentLocator(ref nextComponentLocator);
            componentEmissions.Add(new PlannedBatchComponentEmission(
                emissionTextureId,
                materialContainerTarget,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    emissionTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Emission)
                    .ToDictionary(static pair => pair.Key, static pair => PlannedMembers.Literal(pair.Value), StringComparer.Ordinal)));
            materialMembers["EmissiveMap"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(emissionTextureId));
            materialMembers["EmissiveColor"] = PlannedMembers.Literal(
                ResoniteMaterialComponentPolicy.CreateColorMember(new ResoniteColor(1.0, 1.0, 1.0, 1.0)));
        }

        BatchPlanComponentLocator materialComponentId = CreateBatchPlanComponentLocator(ref nextComponentLocator);
        componentEmissions.Add(new PlannedBatchComponentEmission(
            materialComponentId,
            materialContainerTarget,
            ResoniteMaterialComponentPolicy.GetComponentType(material),
            materialMembers));
        return PlannedWorldElementReference.Planned(materialComponentId);
    }

    private static PlannedSyncListMember CreateRendererMaterials(
        IReadOnlyList<PlannedRendererMaterialBinding> materialBindings,
        Dictionary<PlannedMaterialAsset, PlannedWorldElementReference> emittedMaterialTargets)
    {
        return new PlannedSyncListMember(
            materialBindings
                .Select(binding => PlannedMembers.Reference(emittedMaterialTargets[binding.MaterialAsset]))
                .ToList());
    }

    private static PlannedSyncListMember CreateRendererMaterialPropertyBlocks(
        List<PlannedBatchComponentEmission> componentEmissions,
        BatchPlanSlotLocator assetSlotId,
        BatchPlanSlotLocator presentationSlotId,
        IReadOnlyList<PlannedRendererMaterialBinding> materialBindings,
        ref int nextComponentLocator)
    {
        List<PlannedMember> propertyBlocks = [];

        bool hasMaterialPropertyBlockOverride = false;
        foreach (PlannedRendererMaterialBinding materialBinding in materialBindings)
        {
            if (materialBinding is PlannedMainTextureOverrideRendererMaterialBinding mainTextureOverrideBinding)
            {
                hasMaterialPropertyBlockOverride = true;
                propertyBlocks.Add(
                    CreateMainTexturePropertyBlockReference(
                        componentEmissions,
                        assetSlotId,
                        presentationSlotId,
                        mainTextureOverrideBinding,
                        ref nextComponentLocator));
                continue;
            }

            propertyBlocks.Add(PlannedMembers.NullReference());
        }

        return hasMaterialPropertyBlockOverride
            ? new PlannedSyncListMember(propertyBlocks)
            : new PlannedSyncListMember([]);
    }

    private static PlannedMember CreateMainTexturePropertyBlockReference(
        List<PlannedBatchComponentEmission> componentEmissions,
        BatchPlanSlotLocator assetSlotId,
        BatchPlanSlotLocator presentationSlotId,
        PlannedMainTextureOverrideRendererMaterialBinding binding,
        ref int nextComponentLocator)
    {
        if (binding is PlannedTerrainMainTextureOverrideRendererMaterialBinding
            {
                SharedMainTexturePropertyBlockComponent: { } sharedMainTexturePropertyBlockComponent,
            })
        {
            return PlannedMembers.Reference(PlannedWorldElementReference.Canonical(sharedMainTexturePropertyBlockComponent));
        }

        PlannedWorldElementReference? sharedTextureTarget = binding is PlannedTerrainMainTextureOverrideRendererMaterialBinding
        {
            SharedMainTextureComponent: { } sharedMainTextureComponent,
        }
            ? PlannedWorldElementReference.Canonical(sharedMainTextureComponent)
            : null;
        BatchPlanComponentLocator? textureId = sharedTextureTarget is null
            ? CreateBatchPlanComponentLocator(ref nextComponentLocator)
            : null;
        ResoniteSceneMaterialConventions.TextureMemberRole textureRole = binding switch
        {
            PlannedAlbedoMainTextureOverrideRendererMaterialBinding =>
                ResoniteSceneMaterialConventions.TextureMemberRole.Albedo,
            PlannedTerrainMainTextureOverrideRendererMaterialBinding =>
                ResoniteSceneMaterialConventions.TextureMemberRole.TerrainMainTextureOverride,
            _ => throw new InvalidOperationException(
                $"Unsupported planned main texture override binding type '{binding.GetType().Name}'."),
        };
        if (textureId is { } plannedTextureId)
        {
            componentEmissions.Add(new PlannedBatchComponentEmission(
                plannedTextureId,
                PlannedSlotTargetReference.PlannedSlot(assetSlotId),
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    binding.MainTexture.AssetUri,
                    textureRole)
                    .ToDictionary(static pair => pair.Key, static pair => PlannedMembers.Literal(pair.Value), StringComparer.Ordinal)));
        }

        BatchPlanComponentLocator propertyBlockId = CreateBatchPlanComponentLocator(ref nextComponentLocator);
        componentEmissions.Add(new PlannedBatchComponentEmission(
            propertyBlockId,
            PlannedSlotTargetReference.PlannedSlot(presentationSlotId),
            "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock",
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["Texture"] = PlannedMembers.Reference(sharedTextureTarget ?? PlannedWorldElementReference.Planned(textureId!.Value)),
            }));

        return PlannedMembers.Reference(PlannedWorldElementReference.Planned(propertyBlockId));
    }

    private static BatchPlanSlotLocator CreateBatchPlanSlotLocator(ref int nextSlotLocator)
    {
        return new BatchPlanSlotLocator(++nextSlotLocator);
    }

    private static BatchPlanComponentLocator CreateBatchPlanComponentLocator(ref int nextComponentLocator)
    {
        return new BatchPlanComponentLocator(++nextComponentLocator);
    }

    private static BatchPlanFieldLocator CreateBatchPlanFieldLocator(ref int nextFieldLocator)
    {
        return new BatchPlanFieldLocator(++nextFieldLocator);
    }
}
