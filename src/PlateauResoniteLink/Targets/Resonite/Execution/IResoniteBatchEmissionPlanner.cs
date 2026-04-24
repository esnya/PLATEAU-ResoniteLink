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
        Dictionary<string, PlannedMember>? terrainGridMeshMembers = null;
        PlannedWorldElementReference? dynamicStaticMeshTarget = null;
        PlannedWorldElementReference? dynamicGridMeshTarget = null;
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
                terrainGridMeshMembers = AddPlannedTerrainGridTextureAndCreateGridMembers(
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
                dynamicStaticMeshTarget = PlannedWorldElementReference.Planned(rendererGeometryComponentId);
                BatchPlanComponentLocator gridMeshComponentId = CreateBatchPlanComponentLocator(ref nextComponentLocator);
                terrainGridMeshMembers = AddPlannedTerrainGridTextureAndCreateGridMembers(
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
                rendererGeometryComponentId = gridMeshComponentId;
                dynamicGridMeshTarget = PlannedWorldElementReference.Planned(gridMeshComponentId);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported planned geometry asset type '{emissionPlan.GeometryAsset.GetType().Name}'.");
        }
        componentResolutionTargets.Add(rendererGeometryComponentId);

        Dictionary<MaterialIdentity, PlannedWorldElementReference> emittedMaterialTargets = new();
        foreach (PlannedMaterialAsset materialAsset in emissionPlan.MaterialAssets)
        {
            switch (materialAsset)
            {
                case PlannedReusableMaterialAsset reusableMaterial:
                    emittedMaterialTargets[reusableMaterial.Identity] = PlannedWorldElementReference.Canonical(reusableMaterial.Target);
                    break;
                case PlannedDedicatedMaterialAsset dedicatedMaterial:
                    PlannedWorldElementReference emittedMaterialTarget = AddPlannedDedicatedMaterialEmissions(
                        slotEmissions,
                        componentEmissions,
                        PlannedSlotTargetReference.PlannedSlot(meshAssetSlotId),
                        dedicatedMaterial,
                        ref nextSlotLocator,
                        ref nextComponentLocator);
                    emittedMaterialTargets[dedicatedMaterial.Identity] = emittedMaterialTarget;
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

        if (terrainGridMeshMembers is not null)
        {
            componentEmissions.Add(new PlannedBatchComponentEmission(
                rendererGeometryComponentId,
                PlannedSlotTargetReference.PlannedSlot(presentationSlotId),
                "[FrooxEngine]FrooxEngine.GridMesh",
                terrainGridMeshMembers));
            AddTerrainGridPointsDriverComponents(
                componentEmissions,
                presentationSlotId,
                terrainGridMeshMembers,
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
                    ResolveInitialMeshTarget(dynamicGridMeshTarget, rendererGeometryComponentId)),
                ["Materials"] = CreateRendererMaterials(emissionPlan.Renderer.MaterialBindings, emittedMaterialTargets),
                ["MaterialPropertyBlocks"] = CreateRendererMaterialPropertyBlocks(
                    componentEmissions,
                    meshAssetSlotId,
                    presentationSlotId,
                    emissionPlan.Renderer.MaterialBindings,
                    ref nextComponentLocator),
            }));
        if (dynamicStaticMeshTarget is not null && dynamicGridMeshTarget is not null)
        {
            AddDynamicMeshSwitchComponents(
                componentEmissions,
                presentationSlotId,
                rendererMeshFieldId,
                dynamicGridMeshTarget.Value,
                dynamicStaticMeshTarget.Value,
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
                    ResolveInitialMeshTarget(dynamicGridMeshTarget, rendererGeometryComponentId)),
            }));
        if (dynamicStaticMeshTarget is not null && dynamicGridMeshTarget is not null)
        {
            AddDynamicMeshSwitchComponents(
                componentEmissions,
                presentationSlotId,
                colliderMeshFieldId,
                dynamicGridMeshTarget.Value,
                dynamicStaticMeshTarget.Value,
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
        PlannedWorldElementReference? dynamicGridMeshTarget,
        BatchPlanComponentLocator defaultGeometryComponentId)
    {
        return dynamicGridMeshTarget ?? PlannedWorldElementReference.Planned(defaultGeometryComponentId);
    }

    private static Dictionary<string, PlannedMember> AddPlannedTerrainGridTextureAndCreateGridMembers(
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
        Dictionary<string, PlannedMember> terrainGridMeshMembers = new(StringComparer.Ordinal)
        {
            ["Points"] = PlannedMembers.AddressableField(pointsFieldId, new Field_int2
            {
                Value = new int2
                {
                    x = geometry.Width,
                    y = geometry.Height,
                },
            }),
            ["Size"] = PlannedMembers.Literal(new Field_float2
            {
                Value = new float2
                {
                    x = (float)geometry.Size.X,
                    y = (float)geometry.Size.Y,
                },
            }),
            ["DisplacementMagnitude"] = PlannedMembers.Literal(new Field_float
            {
                Value = (float)displacementMagnitude,
            }),
            ["DisplacementTexture"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(heightTextureComponentId)),
        };
        if (uvScale is not null)
        {
            terrainGridMeshMembers["UVScale"] = PlannedMembers.Literal(new Field_float2
            {
                Value = new float2
                {
                    x = (float)uvScale.X,
                    y = (float)uvScale.Y,
                },
            });
        }

        if (uvOffset is not null)
        {
            terrainGridMeshMembers["UVOffset"] = PlannedMembers.Literal(new Field_float2
            {
                Value = new float2
                {
                    x = (float)uvOffset.X,
                    y = (float)uvOffset.Y,
                },
            });
        }

        return terrainGridMeshMembers;
    }

    private static void AddDynamicMeshSwitchComponents(
        List<PlannedBatchComponentEmission> componentEmissions,
        BatchPlanSlotLocator presentationSlotId,
        BatchPlanFieldLocator targetMeshFieldId,
        PlannedWorldElementReference gridMeshTarget,
        PlannedWorldElementReference staticMeshTarget,
        ref int nextComponentLocator,
        ref int nextFieldLocator)
    {
        BatchPlanFieldLocator stateFieldId = CreateBatchPlanFieldLocator(ref nextFieldLocator);
        Dictionary<string, PlannedMember> switchMembers = new(StringComparer.Ordinal)
        {
            ["State"] = PlannedMembers.AddressableField(stateFieldId, new Field_bool
            {
                Value = false,
            }),
            ["Target"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(targetMeshFieldId)),
            ["FalseTarget"] = PlannedMembers.Reference(gridMeshTarget),
            ["TrueTarget"] = PlannedMembers.Reference(staticMeshTarget),
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
                ["Target"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(stateFieldId)),
                ["DefaultValue"] = PlannedMembers.Literal(new Field_bool
                {
                    Value = false,
                }),
            }));
    }

    private static void AddTerrainGridPointsDriverComponents(
        List<PlannedBatchComponentEmission> componentEmissions,
        BatchPlanSlotLocator presentationSlotId,
        IReadOnlyDictionary<string, PlannedMember> gridMeshMembers,
        ref int nextComponentLocator,
        ref int nextFieldLocator)
    {
        (BatchPlanFieldLocator gridPointsId, Field_int2 gridPoints) = AssertTerrainGridPointsMember(gridMeshMembers);
        BatchPlanFieldLocator progressFieldId = CreateBatchPlanFieldLocator(ref nextFieldLocator);
        componentEmissions.Add(new PlannedBatchComponentEmission(
            CreateBatchPlanComponentLocator(ref nextComponentLocator),
            PlannedSlotTargetReference.PlannedSlot(presentationSlotId),
            ValueGradientDriverInt2ComponentType,
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["Progress"] = PlannedMembers.AddressableField(progressFieldId, new Field_float
                {
                    Value = 1.0f,
                }),
                ["Target"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(gridPointsId)),
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
                    PlannedMembers.Literal(CreateTerrainGridPointsGradientPoint(1.0f, gridPoints.Value))),
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
                ["Target"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(progressFieldId)),
                ["DefaultValue"] = PlannedMembers.Literal(new Field_float
                {
                    Value = 1.0f,
                }),
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

    private static (BatchPlanFieldLocator Identity, Field_int2 Field) AssertTerrainGridPointsMember(
        IReadOnlyDictionary<string, PlannedMember> gridMeshMembers)
    {
        return gridMeshMembers.TryGetValue("Points", out PlannedMember? pointsMember)
            && pointsMember is PlannedAddressableFieldMember { Identity: var identity, Value: Field_int2 points }
            ? (identity, points)
            : throw new InvalidOperationException("Terrain GridMesh Points member must be planned as an addressable field.");
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
            string materialSlotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material, useCommonMaterialAssets: false);
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

        Uri? albedoTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "albedo");
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

        Uri? normalTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "normal");
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

        Uri? heightTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "height");
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

        Uri? metallicTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "metallic");
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

        Uri? emissionTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "emission");
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
        Dictionary<MaterialIdentity, PlannedWorldElementReference> emittedMaterialTargets)
    {
        return new PlannedSyncListMember(
            materialBindings
                .Select(binding => PlannedMembers.Reference(emittedMaterialTargets[binding.MaterialIdentity]))
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
        BatchPlanComponentLocator textureId = CreateBatchPlanComponentLocator(ref nextComponentLocator);
        ResoniteSceneMaterialConventions.TextureMemberRole textureRole = binding.ClampWrapMode
            ? ResoniteSceneMaterialConventions.TextureMemberRole.TerrainMainTextureOverride
            : ResoniteSceneMaterialConventions.TextureMemberRole.Albedo;
        componentEmissions.Add(new PlannedBatchComponentEmission(
            textureId,
            PlannedSlotTargetReference.PlannedSlot(assetSlotId),
            "[FrooxEngine]FrooxEngine.StaticTexture2D",
            ResoniteSceneMaterialConventions.CreateTextureMembers(
                binding.MainTexture.AssetUri,
                textureRole)
                .ToDictionary(static pair => pair.Key, static pair => PlannedMembers.Literal(pair.Value), StringComparer.Ordinal)));

        BatchPlanComponentLocator propertyBlockId = CreateBatchPlanComponentLocator(ref nextComponentLocator);
        componentEmissions.Add(new PlannedBatchComponentEmission(
            propertyBlockId,
            PlannedSlotTargetReference.PlannedSlot(presentationSlotId),
            "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock",
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["Texture"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(textureId)),
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
