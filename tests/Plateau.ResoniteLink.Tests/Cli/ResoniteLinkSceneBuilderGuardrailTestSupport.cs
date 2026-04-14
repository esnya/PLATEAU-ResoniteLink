using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

internal static class ResoniteLinkSceneBuilderGuardrailTestSupport
{
    internal sealed record CapturedScene(
        ResoniteConstructionMetadata Metadata,
        IReadOnlyList<ResoniteConstructionCityObject> CityObjects);

    internal sealed class FakeSession
    {
        private int nextComponentId;
        private int nextSlotId;

        public object Gate { get; } = new();

        public List<AddComponent> AddedComponents { get; } = [];

        public List<AddSlot> AddedSlots { get; } = [];

        public List<IReadOnlyList<DataModelOperation>> Batches { get; } = [];

        public Dictionary<string, Component> ComponentsById { get; } = new(StringComparer.Ordinal);

        public List<ImportMeshRawData> ImportedMeshes { get; } = [];

        public List<string> ImportedTexturePaths { get; } = [];

        public List<ResoniteRawTextureImport> ImportedRawTextures { get; } = [];

        public List<ResoniteRawHdrTextureImport> ImportedRawHdrTextures { get; } = [];

        public Dictionary<string, string> SlotPaths { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Slot> SlotsById { get; } = new(StringComparer.Ordinal);

        public string AllocateComponentId()
        {
            return $"srv_component_{Interlocked.Increment(ref nextComponentId)}";
        }

        public string AllocateSlotId()
        {
            return $"srv_slot_{Interlocked.Increment(ref nextSlotId)}";
        }
    }

    internal sealed class FakeClient(FakeSession session, bool omitComponentDataFromGetSlot = false) : IResoniteLinkClient
    {
        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string createdComponentId = session.AllocateComponentId();

            lock (session.Gate)
            {
                request.Data.ID = createdComponentId;
                session.ComponentsById[createdComponentId] = request.Data;

                if (session.SlotsById.TryGetValue(request.ContainerSlotId, out Slot? containerSlot))
                {
                    containerSlot.Components ??= [];
                    containerSlot.Components.Add(request.Data);
                }

                session.AddedComponents.Add(request);
            }

            return Task.FromResult(createdComponentId);
        }

        public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string createdSlotId = string.IsNullOrWhiteSpace(request.Data.ID)
                ? session.AllocateSlotId()
                : request.Data.ID;

            lock (session.Gate)
            {
                request.Data.ID = createdSlotId;
                session.SlotsById[createdSlotId] = request.Data;
                session.AddedSlots.Add(request);
                session.SlotPaths[createdSlotId] = CreateSlotPath(session.SlotPaths, request.Data);
            }

            return Task.FromResult(createdSlotId);
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (session.Gate)
            {
                session.Batches.Add(operations.ToArray());
            }

            return ExecuteBatchOperationsAsync(
                operations,
                session.AllocateSlotId,
                session.AllocateComponentId,
                AddSlotAsync,
                AddComponentAsync,
                UpdateComponentAsync,
                cancellationToken);
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.ComponentsById.TryGetValue(componentId, out Component? component);
                return Task.FromResult(component);
            }
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (session.Gate)
            {
                if (string.Equals(slotId, "Root", StringComparison.Ordinal))
                {
                    return Task.FromResult<Slot?>(CreateSyntheticRootSlot(session, depth, CloneSlot));
                }

                session.SlotsById.TryGetValue(slotId, out Slot? slot);
                return Task.FromResult(slot is null ? null : CloneSlot(slot, depth));
            }
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.ImportedMeshes.Add(request);
                return Task.FromResult(new Uri($"resdb:///mesh/{session.ImportedMeshes.Count - 1}", UriKind.Absolute));
            }
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (session.Gate)
            {
                switch (textureImport)
                {
                    case ResoniteFileTextureImport fileImport:
                        session.ImportedTexturePaths.Add(fileImport.AbsolutePath);
                        break;
                    case ResoniteRawTextureImport rawImport:
                        session.ImportedRawTextures.Add(rawImport);
                        if (rawImport.Identity is not null)
                        {
                            session.ImportedTexturePaths.Add(rawImport.Identity);
                        }

                        break;
                    case ResoniteRawHdrTextureImport rawHdrImport:
                        session.ImportedRawHdrTextures.Add(rawHdrImport);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported texture import type '{textureImport.GetType().Name}'.");
                }

                int textureIndex = session.ImportedTexturePaths.Count + session.ImportedRawTextures.Count + session.ImportedRawHdrTextures.Count - 1;
                return Task.FromResult(new Uri($"resdb:///texture/{textureIndex}", UriKind.Absolute));
            }
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (session.Gate)
            {
                Component existing = session.ComponentsById[request.Data.ID];
                foreach ((string memberName, Member member) in request.Data.Members)
                {
                    existing.Members[memberName] = member;
                }
            }

            return Task.CompletedTask;
        }

        private Slot CloneSlot(Slot source, int depth)
        {
            Slot clone = new()
            {
                ID = source.ID,
                Parent = source.Parent,
                Name = source.Name,
                Tag = source.Tag,
                Position = source.Position,
                Components = omitComponentDataFromGetSlot ? null : source.Components,
            };

            if (depth <= 0)
            {
                return clone;
            }

            clone.Children = session.SlotsById.Values
                .Where(slot => string.Equals(slot.Parent?.TargetID, source.ID, StringComparison.Ordinal))
                .Select(slot => CloneSlot(slot, depth - 1))
                .ToList();
            return clone;
        }
    }

    internal static async Task<IReadOnlyList<string>> RunBuilderAsync(
        ResoniteLinkSceneBuilder builder,
        CapturedScene scene)
    {
        using TemporaryDirectory workDirectory = new();
        try
        {
            await builder.BeginAsync(scene.Metadata, workDirectory.Path);
            foreach (ResoniteConstructionCityObject cityObject in scene.CityObjects)
            {
                await builder.ProcessCityObjectAsync(cityObject);
            }

            return await builder.CompleteAsync();
        }
        finally
        {
            await builder.DisposeAsync();
        }
    }

    internal static CapturedScene CreateParentRequestChildBuildingScene()
    {
        return CreateScene(
            requestMeshCode: "533945",
            requestedMeshCodes: ["53394525"],
            localOrigin: new ResoniteLocalOrigin(35.6875, 139.6875, 0.0),
            packageNames: ["bldg"],
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"],
            cityObjects:
            [
                CreateCityObject(
                    slotKey: "bldg_53394525",
                    displayName: "Building 25",
                    packageName: "bldg",
                    actualMeshCode: "53394525",
                    lodLevel: 2,
                    materialKey: "bldg-53394525",
                    sourceObjectKey: "building-25"),
            ]);
    }

    internal static CapturedScene CreateChildRequestChildBuildingScene()
    {
        return CreateScene(
            requestMeshCode: "53394525",
            requestedMeshCodes: ["53394525"],
            localOrigin: new ResoniteLocalOrigin(35.6875, 139.69375, 0.0),
            packageNames: ["bldg"],
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"],
            cityObjects:
            [
                CreateCityObject(
                    slotKey: "bldg_53394525_replay",
                    displayName: "Building 25",
                    packageName: "bldg",
                    actualMeshCode: "53394525",
                    lodLevel: 2,
                    materialKey: "bldg-53394525-replay",
                    sourceObjectKey: "building-25"),
            ]);
    }

    internal static CapturedScene CreateParentRequestSharedDemScene()
    {
        return CreateScene(
            requestMeshCode: "533945",
            requestedMeshCodes: ["533945"],
            localOrigin: new ResoniteLocalOrigin(35.6875, 139.6875, 0.0),
            packageNames: ["dem"],
            sourceFiles: ["udx/dem/533945/plateau_tokyo23ku_dem_533945.gml"],
            cityObjects:
            [
                CreateCityObject(
                    slotKey: "dem_shared_parent",
                    displayName: "Shared Terrain",
                    packageName: "dem",
                    actualMeshCode: "533945",
                    lodLevel: null,
                    materialKey: "dem-parent",
                    sourceObjectKey: "shared-terrain"),
            ]);
    }

    internal static CapturedScene CreateChildRequestSharedDemScene()
    {
        return CreateScene(
            requestMeshCode: "53394525",
            requestedMeshCodes: ["53394525"],
            localOrigin: new ResoniteLocalOrigin(35.6875, 139.69375, 0.0),
            packageNames: ["dem"],
            sourceFiles: ["udx/dem/533945/plateau_tokyo23ku_dem_533945.gml"],
            cityObjects:
            [
                CreateCityObject(
                    slotKey: "dem_shared_child",
                    displayName: "Shared Terrain",
                    packageName: "dem",
                    actualMeshCode: "533945",
                    lodLevel: null,
                    materialKey: "dem-child",
                    sourceObjectKey: "shared-terrain"),
            ]);
    }

    internal static CapturedScene CreateSameDisplayNameDifferentIdentityScene()
    {
        return CreateScene(
            requestMeshCode: "53394525",
            requestedMeshCodes: ["53394525"],
            localOrigin: new ResoniteLocalOrigin(35.6875, 139.69375, 0.0),
            packageNames: ["bldg"],
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"],
            cityObjects:
            [
                CreateCityObject(
                    slotKey: "bldg_same_name_a",
                    displayName: "Repeated Building",
                    packageName: "bldg",
                    actualMeshCode: "53394525",
                    lodLevel: 2,
                    materialKey: "bldg-same-name-a",
                    sourceObjectKey: "building-a"),
                CreateCityObject(
                    slotKey: "bldg_same_name_b",
                    displayName: "Repeated Building",
                    packageName: "bldg",
                    actualMeshCode: "53394525",
                    lodLevel: 2,
                    materialKey: "bldg-same-name-b",
                    sourceObjectKey: "building-b"),
            ]);
    }

    internal static CapturedScene CreateIdentityReplayScene(
        string displayName,
        string slotKey,
        string sourceObjectKey)
    {
        return CreateScene(
            requestMeshCode: "53394525",
            requestedMeshCodes: ["53394525"],
            localOrigin: new ResoniteLocalOrigin(35.6875, 139.69375, 0.0),
            packageNames: ["bldg"],
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"],
            cityObjects:
            [
                CreateCityObject(
                    slotKey: slotKey,
                    displayName: displayName,
                    packageName: "bldg",
                    actualMeshCode: "53394525",
                    lodLevel: 2,
                    materialKey: string.Concat("bldg-", slotKey),
                    sourceObjectKey: sourceObjectKey),
            ]);
    }

    internal static int CountNamedSceneSlots(FakeSession session, string slotName)
    {
        lock (session.Gate)
        {
            return session.SlotsById.Values.Count(slot =>
                string.Equals(slot.Name?.Value, slotName, StringComparison.Ordinal)
                && session.SlotPaths.TryGetValue(slot.ID, out string? path)
                && !path.Contains("/Assets/", StringComparison.Ordinal));
        }
    }

    internal static int CountExactPath(FakeSession session, string slotPath)
    {
        lock (session.Gate)
        {
            return session.SlotPaths.Values.Count(path => string.Equals(path, slotPath, StringComparison.Ordinal));
        }
    }

    private static CapturedScene CreateScene(
        string requestMeshCode,
        IReadOnlyList<string> requestedMeshCodes,
        ResoniteLocalOrigin localOrigin,
        IReadOnlyList<string> packageNames,
        IReadOnlyList<string> sourceFiles,
        IReadOnlyList<ResoniteConstructionCityObject> cityObjects)
    {
        ResoniteConstructionMetadata metadata = new(
            SchemaVersion: "3.0",
            WorldName: $"PLATEAU tokyo23ku {requestMeshCode}",
            Request: new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: requestMeshCode,
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
                ServerUri: null),
            SourceDataset: new PlateauSourceDataset(
                PackageNames: packageNames,
                SourceFiles: sourceFiles,
                TerrainTextureOverlays: [],
                RequestedMeshCodes: requestedMeshCodes),
            Attribution: new ResoniteAttribution(
                DatasetLicense: new ResoniteLicenseComponentMetadata(
                    RequireCredit: true,
                    CreditText: "PLATEAU Open Data Terms",
                    LicenseName: "PLATEAU Open Data Terms",
                    LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
                MaterialLicenses: []),
            LocalOrigin: localOrigin);

        return new CapturedScene(metadata, cityObjects);
    }

    private static ResoniteConstructionCityObject CreateCityObject(
        string slotKey,
        string displayName,
        string packageName,
        string actualMeshCode,
        int? lodLevel,
        string materialKey,
        string sourceObjectKey)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: displayName,
            PackageName: packageName,
            ActualMeshCode: actualMeshCode,
            LodLevel: lodLevel,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: CreateTriangleMesh(materialKey),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: materialKey,
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: sourceObjectKey);
    }

    private static ResoniteImportedMesh CreateTriangleMesh(string materialKey)
    {
        return new ResoniteImportedMesh(
            Vertices:
            [
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
            ],
            Submeshes:
            [
                new ResoniteMeshSubmesh(0, materialKey, [0, 1, 2]),
            ]);
    }

    private static string CreateSlotPath(IReadOnlyDictionary<string, string> slotPaths, Slot slot)
    {
        string slotName = slot.Name?.Value ?? slot.ID;
        if (slot.Parent is null || string.Equals(slot.Parent.TargetID, "Root", StringComparison.Ordinal))
        {
            return slotName;
        }

        return string.Concat(slotPaths[slot.Parent.TargetID], "/", slotName);
    }

    private static Slot CreateSyntheticRootSlot(
        FakeSession session,
        int depth,
        Func<Slot, int, Slot> cloneSlot)
    {
        Slot root = new()
        {
            ID = "Root",
            Name = new Field_string
            {
                Value = "Root",
            },
        };

        if (depth <= 0)
        {
            return root;
        }

        root.Children = session.SlotsById.Values
            .Where(static slot => string.Equals(slot.Parent?.TargetID, "Root", StringComparison.Ordinal))
            .Select(slot => cloneSlot(slot, depth - 1))
            .ToList();
        return root;
    }

    private static async Task<BatchResponse> ExecuteBatchOperationsAsync(
        IReadOnlyList<DataModelOperation> operations,
        Func<string> allocateSlotId,
        Func<string> allocateComponentId,
        Func<AddSlot, CancellationToken, Task<string>> addSlotAsync,
        Func<AddComponent, CancellationToken, Task<string>> addComponentAsync,
        Func<UpdateComponent, CancellationToken, Task> updateComponentAsync,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DataModelOperation> resolvedOperations = ResolveBatchLocalSlotReferences(
            operations,
            allocateSlotId,
            allocateComponentId);
        List<Response> responses = [];

        foreach (DataModelOperation operation in resolvedOperations)
        {
            switch (operation)
            {
                case AddSlot addSlot:
                    responses.Add(new NewEntityId
                    {
                        Success = true,
                        SourceMessageID = addSlot.MessageID,
                        EntityId = await addSlotAsync(addSlot, cancellationToken),
                    });
                    break;
                case AddComponent addComponent:
                    responses.Add(new NewEntityId
                    {
                        Success = true,
                        SourceMessageID = addComponent.MessageID,
                        EntityId = await addComponentAsync(addComponent, cancellationToken),
                    });
                    break;
                case UpdateComponent updateComponent:
                    await updateComponentAsync(updateComponent, cancellationToken);
                    responses.Add(new Response
                    {
                        Success = true,
                        SourceMessageID = updateComponent.MessageID,
                    });
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported batch operation '{operation.GetType().Name}'.");
            }
        }

        return new BatchResponse
        {
            Success = true,
            Responses = responses,
        };
    }

    private static List<DataModelOperation> ResolveBatchLocalSlotReferences(
        IReadOnlyList<DataModelOperation> operations,
        Func<string> allocateSlotId,
        Func<string> allocateComponentId)
    {
        Dictionary<string, string> canonicalIdsByLocalId = new(StringComparer.Ordinal);
        List<DataModelOperation> resolved = new(operations.Count);

        foreach (DataModelOperation operation in operations)
        {
            switch (operation)
            {
                case AddSlot addSlot:
                    {
                        string canonicalSlotId = allocateSlotId();
                        if (!string.IsNullOrWhiteSpace(addSlot.Data.ID))
                        {
                            canonicalIdsByLocalId[addSlot.Data.ID] = canonicalSlotId;
                        }

                        resolved.Add(new AddSlot
                        {
                            MessageID = addSlot.MessageID,
                            Data = new Slot
                            {
                                ID = canonicalSlotId,
                                Parent = addSlot.Data.Parent is null ? null : new Reference
                                {
                                    TargetID = ResolveCanonicalId(addSlot.Data.Parent.TargetID, canonicalIdsByLocalId),
                                },
                                Name = addSlot.Data.Name,
                                Tag = addSlot.Data.Tag,
                                Position = addSlot.Data.Position,
                                Rotation = addSlot.Data.Rotation,
                            },
                        });
                        break;
                    }
                case AddComponent addComponent:
                    {
                        string canonicalComponentId = allocateComponentId();
                        if (!string.IsNullOrWhiteSpace(addComponent.Data.ID))
                        {
                            canonicalIdsByLocalId[addComponent.Data.ID] = canonicalComponentId;
                        }

                        resolved.Add(new AddComponent
                        {
                            MessageID = addComponent.MessageID,
                            ContainerSlotId = ResolveCanonicalId(addComponent.ContainerSlotId, canonicalIdsByLocalId),
                            Data = new Component
                            {
                                ID = canonicalComponentId,
                                ComponentType = addComponent.Data.ComponentType,
                                Members = addComponent.Data.Members.ToDictionary(
                                    static pair => pair.Key,
                                    pair => CloneMemberWithResolvedReferences(pair.Value, canonicalIdsByLocalId),
                                    StringComparer.Ordinal),
                            },
                        });
                        break;
                    }
                case UpdateComponent updateComponent:
                    resolved.Add(new UpdateComponent
                    {
                        MessageID = updateComponent.MessageID,
                        Data = new Component
                        {
                            ID = ResolveCanonicalId(updateComponent.Data.ID, canonicalIdsByLocalId),
                            ComponentType = updateComponent.Data.ComponentType,
                            Members = updateComponent.Data.Members.ToDictionary(
                                static pair => pair.Key,
                                pair => CloneMemberWithResolvedReferences(pair.Value, canonicalIdsByLocalId),
                                StringComparer.Ordinal),
                        },
                    });
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported batch operation '{operation.GetType().Name}'.");
            }
        }

        return resolved;
    }

    private static string ResolveCanonicalId(string targetId, IReadOnlyDictionary<string, string> canonicalIdsByLocalId)
    {
        return canonicalIdsByLocalId.TryGetValue(targetId, out string? canonicalId) ? canonicalId : targetId;
    }

    private static Member CloneMemberWithResolvedReferences(
        Member member,
        IReadOnlyDictionary<string, string> canonicalIdsByLocalId)
    {
        return member switch
        {
            Reference reference => new Reference
            {
                TargetID = ResolveCanonicalId(reference.TargetID, canonicalIdsByLocalId),
            },
            SyncList syncList => new SyncList
            {
                Elements = syncList.Elements
                    .Select(element => CloneMemberWithResolvedReferences(element, canonicalIdsByLocalId))
                    .ToList(),
            },
            EmptyElement => new EmptyElement(),
            Field_bool value => new Field_bool { Value = value.Value },
            Field_string value => new Field_string { Value = value.Value },
            Field_float value => new Field_float { Value = value.Value },
            Field_float2 value => new Field_float2 { Value = value.Value },
            Field_float3 value => new Field_float3 { Value = value.Value },
            Field_floatQ value => new Field_floatQ { Value = value.Value },
            Field_int2 value => new Field_int2 { Value = value.Value },
            Field_Enum value => new Field_Enum { Value = value.Value },
            Field_Nullable_Enum value => new Field_Nullable_Enum { Value = value.Value },
            Field_Uri value => new Field_Uri { Value = value.Value },
            Field_colorX value => new Field_colorX { Value = value.Value },
            _ => member,
        };
    }
}
