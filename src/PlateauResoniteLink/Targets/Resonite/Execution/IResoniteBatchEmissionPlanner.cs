using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

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

    public PlannedBatchEmission Create(
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots,
        PlannedSceneObjectEmission emissionPlan)
    {
        ArgumentNullException.ThrowIfNull(objectSlots);
        ArgumentNullException.ThrowIfNull(emissionPlan);

        List<PlannedBatchSlotEmission> slotEmissions = [];
        List<PlannedBatchComponentEmission> componentEmissions = [];
        List<BatchPlanEntityId> slotResolutionTargets = [];
        List<BatchPlanEntityId> componentResolutionTargets = [];

        BatchPlanEntityId meshAssetSlotId = CreateBatchPlanEntityId("mesh-asset-slot");
        slotEmissions.Add(new PlannedBatchSlotEmission(
            meshAssetSlotId,
            objectSlots.AssetLodSlot.SlotId,
            emissionPlan.GeometryAsset.MeshAssetSlotName,
            null,
            null));
        slotResolutionTargets.Add(meshAssetSlotId);

        BatchPlanEntityId geometryComponentId = CreateBatchPlanEntityId("geometry-component");
        switch (emissionPlan.GeometryAsset)
        {
            case PlannedTriangleMeshGeometryAsset triangleMesh:
                componentEmissions.Add(new PlannedBatchComponentEmission(
                    geometryComponentId,
                    meshAssetSlotId.Value,
                    "[FrooxEngine]FrooxEngine.StaticMesh",
                    new Dictionary<string, Member>(StringComparer.Ordinal)
                    {
                        ["URL"] = new Field_Uri
                        {
                            Value = triangleMesh.MeshUri,
                        },
                    }));
                break;
            case PlannedHeightMapGridGeometryAsset heightMap:
                BatchPlanEntityId heightMapAssetSlotId = CreateBatchPlanEntityId("heightmap-asset-slot");
                BatchPlanEntityId heightTextureComponentId = CreateBatchPlanEntityId("height-texture-component");
                slotEmissions.Add(new PlannedBatchSlotEmission(
                    heightMapAssetSlotId,
                    objectSlots.AssetLodSlot.SlotId,
                    heightMap.HeightMapAssetSlotName,
                    null,
                    null));
                slotResolutionTargets.Add(heightMapAssetSlotId);
                componentEmissions.Add(new PlannedBatchComponentEmission(
                    heightTextureComponentId,
                    heightMapAssetSlotId.Value,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    ResoniteGeometryAssetAssembler.CreateHeightMapTextureMembers(heightMap.HeightTextureUri)));
                double displacementMagnitude = Math.Max(heightMap.Geometry.MaxHeight - heightMap.Geometry.MinHeight, 0.0);
                Dictionary<string, Member> gridMeshMembers = new(StringComparer.Ordinal)
                {
                    ["Points"] = new Field_int2
                    {
                        Value = new int2
                        {
                            x = heightMap.Geometry.Width,
                            y = heightMap.Geometry.Height,
                        },
                    },
                    ["Size"] = new Field_float2
                    {
                        Value = new float2
                        {
                            x = (float)heightMap.Geometry.Size.X,
                            y = (float)heightMap.Geometry.Size.Y,
                        },
                    },
                    ["DisplacementMagnitude"] = new Field_float
                    {
                        Value = (float)displacementMagnitude,
                    },
                    ["DisplacementTexture"] = new Reference
                    {
                        TargetID = heightTextureComponentId.Value,
                    },
                };
                if (heightMap.UvScale is not null)
                {
                    gridMeshMembers["UVScale"] = new Field_float2
                    {
                        Value = new float2
                        {
                            x = (float)heightMap.UvScale.X,
                            y = (float)heightMap.UvScale.Y,
                        },
                    };
                }

                if (heightMap.UvOffset is not null)
                {
                    gridMeshMembers["UVOffset"] = new Field_float2
                    {
                        Value = new float2
                        {
                            x = (float)heightMap.UvOffset.X,
                            y = (float)heightMap.UvOffset.Y,
                        },
                    };
                }

                componentEmissions.Add(new PlannedBatchComponentEmission(
                    geometryComponentId,
                    meshAssetSlotId.Value,
                    "[FrooxEngine]FrooxEngine.GridMesh",
                    gridMeshMembers));
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported planned geometry asset type '{emissionPlan.GeometryAsset.GetType().Name}'.");
        }
        componentResolutionTargets.Add(geometryComponentId);

        Dictionary<MaterialIdentity, string> emittedMaterialTargets = new();
        foreach (PlannedMaterialAsset materialAsset in emissionPlan.MaterialAssets)
        {
            switch (materialAsset)
            {
                case PlannedReusableMaterialAsset reusableMaterial:
                    emittedMaterialTargets[reusableMaterial.Identity] = reusableMaterial.TargetId;
                    break;
                case PlannedDedicatedMaterialAsset dedicatedMaterial:
                    string emittedMaterialTarget = AddPlannedDedicatedMaterialEmissions(
                        slotEmissions,
                        componentEmissions,
                        meshAssetSlotId.Value,
                        dedicatedMaterial);
                    emittedMaterialTargets[dedicatedMaterial.Identity] = emittedMaterialTarget;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported planned material asset type '{materialAsset.GetType().Name}'.");
            }
        }

        BatchPlanEntityId presentationSlotId = CreateBatchPlanEntityId("presentation-slot");
        slotEmissions.Add(new PlannedBatchSlotEmission(
            presentationSlotId,
            objectSlots.LodSlot.SlotId,
            objectSlots.CityObjectSlotName,
            objectSlots.CityObjectLocalPosition,
            objectSlots.CityObjectRotation));
        slotResolutionTargets.Add(presentationSlotId);

        componentEmissions.Add(new PlannedBatchComponentEmission(
            CreateBatchPlanEntityId("mesh-renderer-component"),
            presentationSlotId.Value,
            "[FrooxEngine]FrooxEngine.MeshRenderer",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Mesh"] = new Reference
                {
                    TargetID = geometryComponentId.Value,
                },
                ["Materials"] = CreateRendererMaterials(emissionPlan.Renderer.MaterialBindings, emittedMaterialTargets),
                ["MaterialPropertyBlocks"] = CreateRendererMaterialPropertyBlocks(
                    componentEmissions,
                    meshAssetSlotId.Value,
                    presentationSlotId.Value,
                    emissionPlan.Renderer.MaterialBindings),
            }));
        componentEmissions.Add(new PlannedBatchComponentEmission(
            CreateBatchPlanEntityId("mesh-collider-component"),
            presentationSlotId.Value,
            "[FrooxEngine]FrooxEngine.MeshCollider",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Type"] = new Field_Enum
                {
                    Value = emissionPlan.Collider.CollisionEnabled ? "Static" : "NoCollision",
                },
                ["CharacterCollider"] = new Field_bool
                {
                    Value = emissionPlan.Collider.CollisionEnabled,
                },
                ["Mesh"] = new Reference
                {
                    TargetID = geometryComponentId.Value,
                },
            }));

        return new PlannedBatchEmission(
            slotEmissions,
            componentEmissions,
            slotResolutionTargets,
            componentResolutionTargets);
    }

    private static string AddPlannedDedicatedMaterialEmissions(
        List<PlannedBatchSlotEmission> slotEmissions,
        List<PlannedBatchComponentEmission> componentEmissions,
        string meshAssetSlotTargetId,
        PlannedDedicatedMaterialAsset plannedMaterial)
    {
        ResoniteMaterialBinding material = plannedMaterial.Material;
        string materialContainerId = meshAssetSlotTargetId;
        if (plannedMaterial.PreserveDedicatedMaterialSlot)
        {
            BatchPlanEntityId materialSlotId = CreateBatchPlanEntityId($"material-slot:{plannedMaterial.Identity.Value}");
            string materialSlotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material, useCommonMaterialAssets: false);
            slotEmissions.Add(new PlannedBatchSlotEmission(
                materialSlotId,
                meshAssetSlotTargetId,
                materialSlotName,
                null,
                null));
            materialContainerId = materialSlotId.Value;
        }

        Dictionary<string, Member> materialMembers = ResoniteMaterialComponentPolicy.CreateMembers(material);

        Uri? albedoTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "albedo");
        if (albedoTextureUri is not null)
        {
            BatchPlanEntityId albedoTextureId = CreateBatchPlanEntityId($"material-texture:{plannedMaterial.Identity.Value}:albedo");
            componentEmissions.Add(new PlannedBatchComponentEmission(
                albedoTextureId,
                materialContainerId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    albedoTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Albedo)));
            materialMembers["AlbedoTexture"] = new Reference
            {
                TargetID = albedoTextureId.Value,
            };
        }

        Uri? normalTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "normal");
        if (normalTextureUri is not null)
        {
            BatchPlanEntityId normalTextureId = CreateBatchPlanEntityId($"material-texture:{plannedMaterial.Identity.Value}:normal");
            componentEmissions.Add(new PlannedBatchComponentEmission(
                normalTextureId,
                materialContainerId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    normalTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Normal)));
            materialMembers["NormalMap"] = new Reference
            {
                TargetID = normalTextureId.Value,
            };
            materialMembers["NormalScale"] = new Field_float
            {
                Value = DefaultNormalScale,
            };
        }

        Uri? heightTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "height");
        if (heightTextureUri is not null)
        {
            BatchPlanEntityId heightTextureId = CreateBatchPlanEntityId($"material-texture:{plannedMaterial.Identity.Value}:height");
            componentEmissions.Add(new PlannedBatchComponentEmission(
                heightTextureId,
                materialContainerId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    heightTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Height)));
            materialMembers["HeightMap"] = new Reference
            {
                TargetID = heightTextureId.Value,
            };
            materialMembers["HeightScale"] = new Field_float
            {
                Value = DefaultBundledHeightScale,
            };
        }

        Uri? metallicTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "metallic");
        if (metallicTextureUri is not null)
        {
            BatchPlanEntityId metallicTextureId = CreateBatchPlanEntityId($"material-texture:{plannedMaterial.Identity.Value}:metallic");
            componentEmissions.Add(new PlannedBatchComponentEmission(
                metallicTextureId,
                materialContainerId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    metallicTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Metallic)));
            materialMembers["MetallicMap"] = new Reference
            {
                TargetID = metallicTextureId.Value,
            };
            materialMembers["OcclusionMap"] = new Reference
            {
                TargetID = metallicTextureId.Value,
            };
        }

        Uri? emissionTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "emission");
        if (emissionTextureUri is not null)
        {
            BatchPlanEntityId emissionTextureId = CreateBatchPlanEntityId($"material-texture:{plannedMaterial.Identity.Value}:emission");
            componentEmissions.Add(new PlannedBatchComponentEmission(
                emissionTextureId,
                materialContainerId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    emissionTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Emission)));
            materialMembers["EmissiveMap"] = new Reference
            {
                TargetID = emissionTextureId.Value,
            };
            materialMembers["EmissiveColor"] = ResoniteMaterialComponentPolicy.CreateColorMember(
                new ResoniteColor(1.0, 1.0, 1.0, 1.0));
        }

        BatchPlanEntityId materialComponentId = CreateBatchPlanEntityId($"material-component:{plannedMaterial.Identity.Value}");
        componentEmissions.Add(new PlannedBatchComponentEmission(
            materialComponentId,
            materialContainerId,
            ResoniteMaterialComponentPolicy.GetComponentType(material),
            materialMembers));
        return materialComponentId.Value;
    }

    private static SyncList CreateRendererMaterials(
        IReadOnlyList<PlannedRendererMaterialBinding> materialBindings,
        Dictionary<MaterialIdentity, string> emittedMaterialTargets)
    {
        return new SyncList
        {
            Elements = materialBindings
                .Select(binding => (Member)new Reference
                {
                    TargetID = emittedMaterialTargets[binding.MaterialIdentity],
                })
                .ToList(),
        };
    }

    private static SyncList CreateRendererMaterialPropertyBlocks(
        List<PlannedBatchComponentEmission> componentEmissions,
        string assetSlotId,
        string presentationSlotId,
        IReadOnlyList<PlannedRendererMaterialBinding> materialBindings)
    {
        SyncList propertyBlocks = new()
        {
            Elements = [],
        };

        bool hasMaterialPropertyBlockOverride = false;
        foreach (PlannedRendererMaterialBinding materialBinding in materialBindings)
        {
            if (materialBinding is PlannedMainTextureOverrideRendererMaterialBinding mainTextureOverrideBinding)
            {
                hasMaterialPropertyBlockOverride = true;
                propertyBlocks.Elements.Add(
                    CreateMainTexturePropertyBlockReference(
                        componentEmissions,
                        assetSlotId,
                        presentationSlotId,
                        mainTextureOverrideBinding));
                continue;
            }

            propertyBlocks.Elements.Add(new Reference
            {
                TargetID = null,
            });
        }

        return hasMaterialPropertyBlockOverride
            ? propertyBlocks
            : new SyncList { Elements = [] };
    }

    private static Reference CreateMainTexturePropertyBlockReference(
        List<PlannedBatchComponentEmission> componentEmissions,
        string assetSlotId,
        string presentationSlotId,
        PlannedMainTextureOverrideRendererMaterialBinding binding)
    {
        string overrideIdentity = $"{binding.MaterialIdentity.Value}:{binding.MainTexture.Identity.Value}";
        BatchPlanEntityId textureId = CreateBatchPlanEntityId($"renderer-texture:{overrideIdentity}");
        ResoniteSceneMaterialConventions.TextureMemberRole textureRole = binding.ClampWrapMode
            ? ResoniteSceneMaterialConventions.TextureMemberRole.TerrainMainTextureOverride
            : ResoniteSceneMaterialConventions.TextureMemberRole.Albedo;
        componentEmissions.Add(new PlannedBatchComponentEmission(
            textureId,
            assetSlotId,
            "[FrooxEngine]FrooxEngine.StaticTexture2D",
            ResoniteSceneMaterialConventions.CreateTextureMembers(
                binding.MainTexture.AssetUri,
                textureRole)));

        BatchPlanEntityId propertyBlockId = CreateBatchPlanEntityId($"renderer-main-texture-property-block:{overrideIdentity}");
        componentEmissions.Add(new PlannedBatchComponentEmission(
            propertyBlockId,
            presentationSlotId,
            "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Texture"] = new Reference
                {
                    TargetID = textureId.Value,
                },
            }));

        return new Reference
        {
            TargetID = propertyBlockId.Value,
        };
    }

    private static BatchPlanEntityId CreateBatchPlanEntityId(string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suffix);
        return new BatchPlanEntityId($"plan:{suffix}");
    }
}
