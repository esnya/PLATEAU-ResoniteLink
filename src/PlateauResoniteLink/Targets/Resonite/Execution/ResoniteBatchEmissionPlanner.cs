using System;
using System.Collections.Generic;
using System.Linq;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal static class ResoniteBatchEmissionPlanner
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
    private static readonly Field_floatQ TerrainGridRotation = new()
    {
        Value = new floatQ
        {
            x = (float)Math.Sqrt(0.5),
            y = 0.0f,
            z = 0.0f,
            w = (float)Math.Sqrt(0.5),
        },
    };

    public static PlannedBatchEmission Create(
        ResoniteObjectSlotHierarchy objectSlots,
        PlannedGeometryAsset geometryAsset,
        IReadOnlyList<PlannedMaterialAsset> materialAssets,
        IReadOnlyList<PlannedRendererMaterialBinding> rendererMaterialBindings,
        bool collisionEnabled)
    {
        ArgumentNullException.ThrowIfNull(objectSlots);
        ArgumentNullException.ThrowIfNull(geometryAsset);
        ArgumentNullException.ThrowIfNull(materialAssets);
        ArgumentNullException.ThrowIfNull(rendererMaterialBindings);

        List<PlannedBatchSlotEmission> slotEmissions = [];
        List<PlannedBatchComponentEmission> componentEmissions = [];
        List<PlannedBatchSlotEmission> slotResolutionTargets = [];
        List<PlannedBatchComponentEmission> componentResolutionTargets = [];

        PlannedBatchSlotEmission meshAssetSlot = new(
            PlannedSlotTargetReference.CanonicalSlot(objectSlots.AssetLodSlot.Locator),
            geometryAsset.MeshAssetSlotName,
            null,
            null,
            objectSlots.CityObjectOrderOffset);
        slotEmissions.Add(meshAssetSlot);
        slotResolutionTargets.Add(meshAssetSlot);

        PlannedBatchComponentEmission? rendererGeometryComponent = null;
        PlannedTerrainGridMeshBundle? terrainGridMesh = null;
        PlannedDynamicTerrainMeshBundle? dynamicTerrainMesh = null;
        PlannedWorldElementReference? dynamicStaticMeshTarget = null;
        switch (geometryAsset)
        {
            case PlannedTriangleMeshGeometryAsset triangleMesh:
                rendererGeometryComponent = new PlannedBatchComponentEmission(
                    PlannedSlotTargetReference.PlannedSlot(meshAssetSlot),
                    "[FrooxEngine]FrooxEngine.StaticMesh",
                    new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
                    {
                        ["URL"] = PlannedMembers.Literal(new Field_Uri
                        {
                            Value = triangleMesh.MeshUri,
                        }),
                    });
                componentEmissions.Add(rendererGeometryComponent);
                break;
            case PlannedTerrainGridGeometryAsset heightMap:
                terrainGridMesh = AddPlannedTerrainGridTextureAndCreateGridBundle(
                    slotEmissions,
                    componentEmissions,
                    slotResolutionTargets,
                    objectSlots,
                    heightMap.TerrainGridAssetSlotName,
                    heightMap.Geometry,
                    heightMap.HeightTextureUri,
                    heightMap.UvScale,
                    heightMap.UvOffset);
                break;
            case PlannedDynamicTerrainGeometryAsset dynamicTerrain:
                rendererGeometryComponent = new PlannedBatchComponentEmission(
                    PlannedSlotTargetReference.PlannedSlot(meshAssetSlot),
                    "[FrooxEngine]FrooxEngine.StaticMesh",
                    new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
                    {
                        ["URL"] = PlannedMembers.Literal(new Field_Uri
                        {
                            Value = dynamicTerrain.StaticMeshUri,
                        }),
                    });
                componentEmissions.Add(rendererGeometryComponent);
                dynamicStaticMeshTarget = PlannedWorldElementReference.Planned(rendererGeometryComponent);
                terrainGridMesh = AddPlannedTerrainGridTextureAndCreateGridBundle(
                    slotEmissions,
                    componentEmissions,
                    slotResolutionTargets,
                    objectSlots,
                    dynamicTerrain.TerrainGridAssetSlotName,
                    dynamicTerrain.GridGeometry,
                    dynamicTerrain.HeightTextureUri,
                    dynamicTerrain.UvScale,
                    dynamicTerrain.UvOffset);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported planned geometry asset type '{geometryAsset.GetType().Name}'.");
        }

        Dictionary<PlannedMaterialAsset, PlannedWorldElementReference> emittedMaterialTargets =
            new(ReferenceEqualityComparer.Instance);
        foreach (PlannedMaterialAsset materialAsset in materialAssets)
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
                        PlannedSlotTargetReference.PlannedSlot(meshAssetSlot),
                        dedicatedMaterial);
                    emittedMaterialTargets[dedicatedMaterial] = emittedMaterialTarget;
                    break;
                default:
                    throw new InvalidOperationException(
                    $"Unsupported planned material asset type '{materialAsset.GetType().Name}'.");
            }
        }

        PlannedBatchSlotEmission presentationSlot = new(
            PlannedSlotTargetReference.CanonicalSlot(objectSlots.LodSlot.Locator),
            objectSlots.CityObjectSlotName,
            objectSlots.CityObjectLocalPosition,
            objectSlots.CityObjectRotation,
            objectSlots.CityObjectOrderOffset);
        slotEmissions.Add(presentationSlot);
        slotResolutionTargets.Add(presentationSlot);

        if (terrainGridMesh is not null)
        {
            rendererGeometryComponent = new PlannedBatchComponentEmission(
                PlannedSlotTargetReference.PlannedSlot(presentationSlot),
                "[FrooxEngine]FrooxEngine.GridMesh",
                CreateTerrainGridMeshMembers(terrainGridMesh));
            componentEmissions.Add(rendererGeometryComponent);
            if (dynamicStaticMeshTarget is not null)
            {
                dynamicTerrainMesh = new PlannedDynamicTerrainMeshBundle(
                    PlannedWorldElementReference.Planned(rendererGeometryComponent),
                    dynamicStaticMeshTarget);
            }

            AddTerrainGridPointsDriverComponents(
                componentEmissions,
                presentationSlot,
                terrainGridMesh);
        }

        if (rendererGeometryComponent is null)
        {
            throw new InvalidOperationException("Planned scene object emission did not produce a renderer geometry component.");
        }

        componentResolutionTargets.Add(rendererGeometryComponent);

        PlannedFieldReference rendererMeshField = new();
        componentEmissions.Add(new PlannedBatchComponentEmission(
            PlannedSlotTargetReference.PlannedSlot(presentationSlot),
            "[FrooxEngine]FrooxEngine.MeshRenderer",
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["Mesh"] = PlannedMembers.AddressableReference(
                    rendererMeshField,
                    ResolveInitialMeshTarget(dynamicTerrainMesh, rendererGeometryComponent)),
                ["Materials"] = CreateRendererMaterials(rendererMaterialBindings, emittedMaterialTargets),
                ["MaterialPropertyBlocks"] = CreateRendererMaterialPropertyBlocks(
                    componentEmissions,
                    meshAssetSlot,
                    presentationSlot,
                    rendererMaterialBindings),
            }));
        if (dynamicTerrainMesh is not null)
        {
            AddDynamicMeshSwitchComponents(
                componentEmissions,
                presentationSlot,
                rendererMeshField,
                dynamicTerrainMesh);
        }

        PlannedFieldReference colliderMeshField = new();
        componentEmissions.Add(new PlannedBatchComponentEmission(
            PlannedSlotTargetReference.PlannedSlot(presentationSlot),
            "[FrooxEngine]FrooxEngine.MeshCollider",
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["Type"] = PlannedMembers.Literal(new Field_Enum
                {
                    Value = collisionEnabled ? "Static" : "NoCollision",
                }),
                ["CharacterCollider"] = PlannedMembers.Literal(new Field_bool
                {
                    Value = collisionEnabled,
                }),
                ["Mesh"] = PlannedMembers.AddressableReference(
                    colliderMeshField,
                    ResolveInitialMeshTarget(dynamicTerrainMesh, rendererGeometryComponent)),
            }));
        if (dynamicTerrainMesh is not null)
        {
            AddDynamicMeshSwitchComponents(
                componentEmissions,
                presentationSlot,
                colliderMeshField,
                dynamicTerrainMesh);
        }

        return PlannedBatchEmission.Create(
            slotEmissions,
            componentEmissions,
            slotResolutionTargets,
            componentResolutionTargets);
    }

    private static PlannedWorldElementReference ResolveInitialMeshTarget(
        PlannedDynamicTerrainMeshBundle? dynamicTerrainMesh,
        PlannedBatchComponentEmission defaultGeometryComponent)
    {
        return dynamicTerrainMesh?.InitialMeshTarget ?? PlannedWorldElementReference.Planned(defaultGeometryComponent);
    }

    private static PlannedTerrainGridMeshBundle AddPlannedTerrainGridTextureAndCreateGridBundle(
        List<PlannedBatchSlotEmission> slotEmissions,
        List<PlannedBatchComponentEmission> componentEmissions,
        List<PlannedBatchSlotEmission> slotResolutionTargets,
        ResoniteObjectSlotHierarchy objectSlots,
        string terrainGridAssetSlotName,
        ResoniteTerrainGridGeometry geometry,
        Uri heightTextureUri,
        ResoniteFloat2? uvScale,
        ResoniteFloat2? uvOffset)
    {
        PlannedBatchSlotEmission heightMapAssetSlot = new(
            PlannedSlotTargetReference.CanonicalSlot(objectSlots.AssetLodSlot.Locator),
            terrainGridAssetSlotName,
            null,
            null);
        slotEmissions.Add(heightMapAssetSlot);
        slotResolutionTargets.Add(heightMapAssetSlot);
        PlannedBatchComponentEmission heightTextureComponent = new(
            PlannedSlotTargetReference.PlannedSlot(heightMapAssetSlot),
            "[FrooxEngine]FrooxEngine.StaticTexture2D",
            ResoniteGeometryAssetAssembler.CreateTerrainGridTextureMembers(heightTextureUri)
                .ToDictionary(static pair => pair.Key, static pair => PlannedMembers.Literal(pair.Value), StringComparer.Ordinal));
        componentEmissions.Add(heightTextureComponent);

        PlannedFieldReference pointsField = new();
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
            Value = -1.0f,
        };
        float firstBoundsY = (float)(geometry.MinHeight * displacement.Value);
        float secondBoundsY = (float)(geometry.MaxHeight * displacement.Value);
        Field_float minBoundsY = new()
        {
            Value = Math.Min(firstBoundsY, secondBoundsY),
        };
        Field_float maxBoundsY = new()
        {
            Value = Math.Max(firstBoundsY, secondBoundsY),
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
            pointsField,
            points,
            size,
            displacement,
            minBoundsY,
            maxBoundsY,
            PlannedWorldElementReference.Planned(heightTextureComponent),
            plannedUvScale,
            plannedUvOffset);
    }

    private static Dictionary<string, PlannedMember> CreateTerrainGridMeshMembers(
        PlannedTerrainGridMeshBundle terrainGridMesh)
    {
        Dictionary<string, PlannedMember> members = new(StringComparer.Ordinal)
        {
            ["OverrideBoundingBox"] = PlannedMembers.Literal(new Field_bool
            {
                Value = true,
            }),
            ["OverridenBoundingBox"] = PlannedMembers.Literal(CreateTerrainGridBoundingBox(terrainGridMesh)),
            ["Points"] = PlannedMembers.AddressableField(terrainGridMesh.PointsField, terrainGridMesh.Points),
            ["Size"] = PlannedMembers.Literal(terrainGridMesh.Size),
            ["Rotation"] = PlannedMembers.Literal(TerrainGridRotation),
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

    private static Field_BoundingBox CreateTerrainGridBoundingBox(PlannedTerrainGridMeshBundle terrainGridMesh)
    {
        float halfWidth = Math.Abs(terrainGridMesh.Size.Value.x) / 2.0f;
        float halfDepth = Math.Abs(terrainGridMesh.Size.Value.y) / 2.0f;
        return new Field_BoundingBox
        {
            Value = new BoundingBox
            {
                min = new float3
                {
                    x = -halfWidth,
                    y = terrainGridMesh.MinBoundsY.Value,
                    z = -halfDepth,
                },
                max = new float3
                {
                    x = halfWidth,
                    y = terrainGridMesh.MaxBoundsY.Value,
                    z = halfDepth,
                },
            },
        };
    }

    private static void AddDynamicMeshSwitchComponents(
        List<PlannedBatchComponentEmission> componentEmissions,
        PlannedBatchSlotEmission presentationSlot,
        PlannedFieldReference targetMeshField,
        PlannedDynamicTerrainMeshBundle dynamicTerrainMesh)
    {
        PlannedDriverTargetBundle stateDriverTarget = PlannedDriverTargetBundle.Create(
            new Field_bool
            {
                Value = false,
            });
        Dictionary<string, PlannedMember> switchMembers = new(StringComparer.Ordinal)
        {
            ["State"] = stateDriverTarget.Field,
            ["Target"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(targetMeshField)),
            ["FalseTarget"] = PlannedMembers.Reference(dynamicTerrainMesh.GridMeshTarget),
            ["TrueTarget"] = PlannedMembers.Reference(dynamicTerrainMesh.StaticMeshTarget),
        };
        componentEmissions.Add(new PlannedBatchComponentEmission(
            PlannedSlotTargetReference.PlannedSlot(presentationSlot),
            BooleanAssetDriverMeshComponentType,
            switchMembers));
        componentEmissions.Add(new PlannedBatchComponentEmission(
            PlannedSlotTargetReference.PlannedSlot(presentationSlot),
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
        PlannedBatchSlotEmission presentationSlot,
        PlannedTerrainGridMeshBundle terrainGridMesh)
    {
        PlannedDriverTargetBundle progressDriverTarget = PlannedDriverTargetBundle.Create(
            new Field_float
            {
                Value = 1.0f,
            });
        componentEmissions.Add(new PlannedBatchComponentEmission(
            PlannedSlotTargetReference.PlannedSlot(presentationSlot),
            ValueGradientDriverInt2ComponentType,
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["Progress"] = progressDriverTarget.Field,
                ["Target"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(terrainGridMesh.PointsField)),
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
            PlannedSlotTargetReference.PlannedSlot(presentationSlot),
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
        PlannedDedicatedMaterialAsset plannedMaterial)
    {
        ResoniteMaterialBinding material = plannedMaterial.Material;
        PlannedSlotTargetReference materialContainerTarget = meshAssetSlotTarget;
        if (plannedMaterial.PreserveDedicatedMaterialSlot)
        {
            string materialSlotName = plannedMaterial.DedicatedMaterialSlotName
                ?? throw new InvalidOperationException("Dedicated material slot preservation requires a planned material-index slot name.");
            PlannedBatchSlotEmission materialSlot = new(
                meshAssetSlotTarget,
                materialSlotName,
                null,
                null);
            slotEmissions.Add(materialSlot);
            materialContainerTarget = PlannedSlotTargetReference.PlannedSlot(materialSlot);
        }

        Dictionary<string, PlannedMember> materialMembers = ResoniteMaterialComponentPolicy.CreateMembers(material)
            .ToDictionary(static pair => pair.Key, static pair => PlannedMembers.Literal(pair.Value), StringComparer.Ordinal);

        _ = AddPlannedTextureComponentReference(
            componentEmissions,
            materialContainerTarget,
            plannedMaterial,
            ResoniteSceneMaterialConventions.PlannedTextureRole.Albedo,
            "AlbedoTexture",
            materialMembers);

        if (AddPlannedTextureComponentReference(
            componentEmissions,
            materialContainerTarget,
            plannedMaterial,
            ResoniteSceneMaterialConventions.PlannedTextureRole.Normal,
            "NormalMap",
            materialMembers) is not null)
        {
            materialMembers["NormalScale"] = PlannedMembers.Literal(new Field_float
            {
                Value = DefaultNormalScale,
            });
        }

        if (AddPlannedTextureComponentReference(
            componentEmissions,
            materialContainerTarget,
            plannedMaterial,
            ResoniteSceneMaterialConventions.PlannedTextureRole.Height,
            "HeightMap",
            materialMembers) is not null)
        {
            materialMembers["HeightScale"] = PlannedMembers.Literal(new Field_float
            {
                Value = DefaultBundledHeightScale,
            });
        }

        PlannedBatchComponentEmission? metallicTexture = AddPlannedTextureComponentReference(
            componentEmissions,
            materialContainerTarget,
            plannedMaterial,
            ResoniteSceneMaterialConventions.PlannedTextureRole.Metallic,
            "MetallicMap",
            materialMembers);
        if (metallicTexture is not null)
        {
            materialMembers["OcclusionMap"] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(metallicTexture));
        }

        if (AddPlannedTextureComponentReference(
            componentEmissions,
            materialContainerTarget,
            plannedMaterial,
            ResoniteSceneMaterialConventions.PlannedTextureRole.Emission,
            "EmissiveMap",
            materialMembers) is not null)
        {
            materialMembers["EmissiveColor"] = PlannedMembers.Literal(
                ResoniteMaterialComponentPolicy.CreateColorMember(new ResoniteColor(1.0, 1.0, 1.0, 1.0)));
        }

        PlannedBatchComponentEmission materialComponent = new(
            materialContainerTarget,
            ResoniteMaterialComponentPolicy.GetComponentType(material),
            materialMembers);
        componentEmissions.Add(materialComponent);
        return PlannedWorldElementReference.Planned(materialComponent);
    }

    private static PlannedBatchComponentEmission? AddPlannedTextureComponentReference(
        List<PlannedBatchComponentEmission> componentEmissions,
        PlannedSlotTargetReference materialContainerTarget,
        PlannedDedicatedMaterialAsset plannedMaterial,
        ResoniteSceneMaterialConventions.PlannedTextureRole textureRole,
        string materialMemberName,
        Dictionary<string, PlannedMember> materialMembers)
    {
        Uri? textureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, textureRole);
        if (textureUri is null)
        {
            return null;
        }

        PlannedBatchComponentEmission textureComponent = new(
            materialContainerTarget,
            "[FrooxEngine]FrooxEngine.StaticTexture2D",
            ResoniteSceneMaterialConventions.CreateTextureMembers(
                textureUri,
                ResoniteSceneMaterialConventions.ToTextureMemberRole(textureRole))
                .ToDictionary(static pair => pair.Key, static pair => PlannedMembers.Literal(pair.Value), StringComparer.Ordinal));
        componentEmissions.Add(textureComponent);
        materialMembers[materialMemberName] = PlannedMembers.Reference(PlannedWorldElementReference.Planned(textureComponent));
        return textureComponent;
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
        PlannedBatchSlotEmission assetSlot,
        PlannedBatchSlotEmission presentationSlot,
        IReadOnlyList<PlannedRendererMaterialBinding> materialBindings)
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
                        assetSlot,
                        presentationSlot,
                        mainTextureOverrideBinding));
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
        PlannedBatchSlotEmission assetSlot,
        PlannedBatchSlotEmission presentationSlot,
        PlannedMainTextureOverrideRendererMaterialBinding binding)
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
        PlannedBatchComponentEmission? textureComponent = null;
        ResoniteSceneMaterialConventions.TextureMemberRole textureRole = binding switch
        {
            PlannedAlbedoMainTextureOverrideRendererMaterialBinding =>
                ResoniteSceneMaterialConventions.TextureMemberRole.Albedo,
            PlannedTerrainMainTextureOverrideRendererMaterialBinding =>
                ResoniteSceneMaterialConventions.TextureMemberRole.TerrainMainTextureOverride,
            _ => throw new InvalidOperationException(
                $"Unsupported planned main texture override binding type '{binding.GetType().Name}'."),
        };
        if (sharedTextureTarget is null)
        {
            textureComponent = new PlannedBatchComponentEmission(
                PlannedSlotTargetReference.PlannedSlot(assetSlot),
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    binding.MainTexture.AssetUri,
                    textureRole)
                    .ToDictionary(static pair => pair.Key, static pair => PlannedMembers.Literal(pair.Value), StringComparer.Ordinal));
            componentEmissions.Add(textureComponent);
        }

        PlannedWorldElementReference textureTarget = sharedTextureTarget
            ?? PlannedWorldElementReference.Planned(textureComponent ?? throw new InvalidOperationException("Main texture property block did not create a texture component."));
        PlannedBatchComponentEmission propertyBlock = new(
            PlannedSlotTargetReference.PlannedSlot(presentationSlot),
            "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock",
            new Dictionary<string, PlannedMember>(StringComparer.Ordinal)
            {
                ["Texture"] = PlannedMembers.Reference(textureTarget),
            });
        componentEmissions.Add(propertyBlock);

        return PlannedMembers.Reference(PlannedWorldElementReference.Planned(propertyBlock));
    }
}
