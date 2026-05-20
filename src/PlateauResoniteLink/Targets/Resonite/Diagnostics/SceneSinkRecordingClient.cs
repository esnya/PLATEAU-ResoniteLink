using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

using TransportComponentLocator = PlateauResoniteLink.Transport.ResoniteLink.ResoniteTransportComponentLocator;
using TransportSlotLocator = PlateauResoniteLink.Transport.ResoniteLink.ResoniteTransportSlotLocator;

namespace PlateauResoniteLink.Targets.Resonite.Diagnostics;

internal sealed record SlotGetRequest(string SlotId, string SlotPath, int Depth);

internal class SceneSinkRecordingClient : IResoniteLinkClient
{
    private readonly object gate = new();
    private int nextComponentId;
    private int nextSlotId;

    public List<AddComponent> AddedComponents { get; } = [];

    public List<AddSlot> AddedSlots { get; } = [];

    public List<ImportMeshRawData> ImportedMeshes { get; } = [];

    public List<ResoniteRawTextureImport> ImportedRawTextures { get; } = [];

    public List<ResoniteRawHdrTextureImport> ImportedRawHdrTextures { get; } = [];

    public List<IReadOnlyList<DataModelOperation>> Batches { get; } = [];

    public List<UpdateComponent> UpdatedComponents { get; } = [];

    public Dictionary<string, Component> ComponentsById { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Slot> SlotsById { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> SlotPaths { get; } = new(StringComparer.Ordinal);

    public List<SlotGetRequest> SlotGetRequests { get; } = [];

    public List<string> OperationNames { get; } = [];

    public int ConnectCallCount { get; private set; }

    public void Dispose()
    {
    }

    public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConnectCallCount++;
        return Task.CompletedTask;
    }

    public Task<ResoniteTransportComponentCreationResult> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string createdComponentId = string.IsNullOrWhiteSpace(request.Data.ID)
            ? AllocateComponentId()
            : request.Data.ID;
        lock (gate)
        {
            request.Data.ID = createdComponentId;
            ComponentsById[createdComponentId] = request.Data;
            if (SlotsById.TryGetValue(request.ContainerSlotId, out Slot? containerSlot))
            {
                containerSlot.Components ??= [];
                containerSlot.Components.Add(request.Data);
            }

            AddedComponents.Add(request);
        }

        return Task.FromResult(
            new ResoniteTransportComponentCreationResult(new TransportComponentLocator(createdComponentId)));
    }

    public Task<ResoniteTransportSlotCreationResult> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string createdSlotId = string.IsNullOrWhiteSpace(request.Data.ID)
            ? AllocateSlotId()
            : request.Data.ID;
        lock (gate)
        {
            request.Data.ID = createdSlotId;
            SlotsById[createdSlotId] = request.Data;
            SlotPaths[createdSlotId] = CreateSlotPath(request.Data);
            AddedSlots.Add(request);
        }

        return Task.FromResult(new ResoniteTransportSlotCreationResult(new TransportSlotLocator(createdSlotId)));
    }

    public async Task<BatchResponse> RunDataModelOperationBatchAsync(
        IReadOnlyList<DataModelOperation> operations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Dictionary<string, string> localSlotIds = operations
            .OfType<AddSlot>()
            .Where(static operation => !string.IsNullOrWhiteSpace(operation.Data.ID))
            .ToDictionary(static operation => operation.Data.ID, _ => AllocateSlotId(), StringComparer.Ordinal);
        Dictionary<string, string> localComponentIds = operations
            .OfType<AddComponent>()
            .Where(static operation => !string.IsNullOrWhiteSpace(operation.Data.ID))
            .ToDictionary(static operation => operation.Data.ID, _ => AllocateComponentId(), StringComparer.Ordinal);

        lock (gate)
        {
            Batches.Add(operations.ToArray());
        }

        List<Response> responses = [];
        foreach (DataModelOperation operation in operations)
        {
            switch (operation)
            {
                case AddSlot addSlot:
                    responses.Add(new NewEntityId
                    {
                        Success = true,
                        SourceMessageID = addSlot.MessageID,
                        EntityId = (await AddSlotAsync(ResolveBatchAddSlot(addSlot, localSlotIds), cancellationToken)).Slot.Value,
                    });
                    break;
                case AddComponent addComponent:
                    responses.Add(new NewEntityId
                    {
                        Success = true,
                        SourceMessageID = addComponent.MessageID,
                        EntityId = (await AddComponentAsync(
                            ResolveBatchAddComponent(addComponent, localSlotIds, localComponentIds),
                            cancellationToken)).Component.Value,
                    });
                    break;
                case UpdateComponent updateComponent:
                    await UpdateComponentAsync(
                        new ResoniteComponentUpdate
                        {
                            Component = new TransportComponentLocator(updateComponent.Data.ID!),
                            Members = new Dictionary<string, Member>(updateComponent.Data.Members, StringComparer.Ordinal),
                        },
                        cancellationToken);
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

    public Task<Component?> GetComponentAsync(TransportComponentLocator component, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ComponentsById.TryGetValue(component.Value, out Component? resolvedComponent);
            return Task.FromResult(resolvedComponent);
        }
    }

    public Task<Slot?> GetSlotAsync(TransportSlotLocator slot, int depth, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            string observedSlot = SlotPaths.TryGetValue(slot.Value, out string? path)
                ? path
                : slot.Value;
            SlotGetRequests.Add(new SlotGetRequest(slot.Value, observedSlot, depth));
            OperationNames.Add($"GetSlot:{observedSlot}");
        }

        if (slot.IsRoot)
        {
            return Task.FromResult<Slot?>(CreateSyntheticRoot(depth));
        }

        lock (gate)
        {
            return Task.FromResult<Slot?>(
                SlotsById.TryGetValue(slot.Value, out Slot? resolvedSlot)
                    ? CloneSlot(resolvedSlot, depth)
                    : null);
        }
    }

    public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ImportedMeshes.Add(request);
            return Task.FromResult(new Uri($"resdb:///mesh/{ImportedMeshes.Count - 1}", UriKind.Absolute));
        }
    }

    public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            OperationNames.Add("ImportTexture");
            switch (textureImport)
            {
                case ResoniteRawTextureImport rawImport:
                    ImportedRawTextures.Add(rawImport);
                    break;
                case ResoniteRawHdrTextureImport rawHdrImport:
                    ImportedRawHdrTextures.Add(rawHdrImport);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported texture import type '{textureImport.GetType().Name}'.");
            }

            return Task.FromResult(new Uri($"resdb:///texture/{ImportedRawTextures.Count + ImportedRawHdrTextures.Count - 1}", UriKind.Absolute));
        }
    }

    public Task UpdateComponentAsync(ResoniteComponentUpdate request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            UpdatedComponents.Add(new UpdateComponent
            {
                Data = new Component
                {
                    ID = request.Component.Value,
                    Members = request.Members.ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value,
                        StringComparer.Ordinal),
                },
            });
            if (!ComponentsById.TryGetValue(request.Component.Value, out Component? existingComponent))
            {
                return Task.CompletedTask;
            }

            foreach ((string memberName, Member member) in request.Members)
            {
                existingComponent.Members[memberName] = member;
            }
        }

        return Task.CompletedTask;
    }

    private string AllocateComponentId()
    {
        return string.Create(CultureInfo.InvariantCulture, $"srv_component_{Interlocked.Increment(ref nextComponentId)}");
    }

    private string AllocateSlotId()
    {
        return string.Create(CultureInfo.InvariantCulture, $"srv_slot_{Interlocked.Increment(ref nextSlotId)}");
    }

    private string CreateSlotPath(Slot slot)
    {
        string slotName = slot.Name?.Value ?? "<unnamed>";
        if (slot.Parent is null || string.IsNullOrWhiteSpace(slot.Parent.TargetID))
        {
            return slotName;
        }

        if (!SlotPaths.TryGetValue(slot.Parent.TargetID, out string? parentPath))
        {
            return slotName;
        }

        return $"{parentPath}/{slotName}";
    }

    private Slot CreateSyntheticRoot(int depth)
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

        lock (gate)
        {
            root.Children = SlotsById.Values
                .Where(slot => string.Equals(slot.Parent?.TargetID, "Root", StringComparison.Ordinal))
                .Select(slot => CloneSlot(slot, depth - 1))
                .ToList();
        }

        return root;
    }

    private Slot CloneSlot(Slot source, int depth)
    {
        Slot clone = new()
        {
            ID = source.ID,
            Parent = source.Parent,
            Name = source.Name,
            Position = source.Position,
            Rotation = source.Rotation,
            Components = source.Components,
        };

        if (depth <= 0)
        {
            return clone;
        }

        clone.Children = SlotsById.Values
            .Where(slot => string.Equals(slot.Parent?.TargetID, source.ID, StringComparison.Ordinal))
            .Select(slot => CloneSlot(slot, depth - 1))
            .ToList();
        return clone;
    }

    private static AddSlot ResolveBatchAddSlot(AddSlot addSlot, IReadOnlyDictionary<string, string> localSlotIds)
    {
        return new AddSlot
        {
            MessageID = addSlot.MessageID,
            Data = new Slot
            {
                ID = TryResolveLocalId(addSlot.Data.ID, localSlotIds),
                Parent = addSlot.Data.Parent is null
                    ? null
                    : new Reference
                    {
                        TargetID = TryResolveLocalId(addSlot.Data.Parent.TargetID, localSlotIds),
                    },
                Name = addSlot.Data.Name,
                Position = addSlot.Data.Position,
                Rotation = addSlot.Data.Rotation,
                Tag = addSlot.Data.Tag,
            },
        };
    }

    private static AddComponent ResolveBatchAddComponent(
        AddComponent addComponent,
        IReadOnlyDictionary<string, string> localSlotIds,
        IReadOnlyDictionary<string, string> localComponentIds)
    {
        return new AddComponent
        {
            MessageID = addComponent.MessageID,
            ContainerSlotId = TryResolveLocalId(addComponent.ContainerSlotId, localSlotIds),
            Data = new Component
            {
                ID = TryResolveLocalId(addComponent.Data.ID, localComponentIds),
                ComponentType = addComponent.Data.ComponentType,
                Members = addComponent.Data.Members.ToDictionary(
                    static pair => pair.Key,
                    pair => ResolveBatchMember(pair.Value, localSlotIds, localComponentIds),
                    StringComparer.Ordinal),
            },
        };
    }

    private static Member ResolveBatchMember(
        Member member,
        IReadOnlyDictionary<string, string> localSlotIds,
        IReadOnlyDictionary<string, string> localComponentIds)
    {
        return member switch
        {
            Reference reference => new Reference
            {
                TargetID = TryResolveLocalId(
                    TryResolveLocalId(reference.TargetID, localSlotIds),
                    localComponentIds),
                TargetType = reference.TargetType,
            },
            SyncList syncList => new SyncList
            {
                Elements = syncList.Elements
                    .Select(element => ResolveBatchMember(element, localSlotIds, localComponentIds))
                    .ToList(),
            },
            _ => member,
        };
    }

    private static string? TryResolveLocalId(string? id, IReadOnlyDictionary<string, string> localIds)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        return localIds.TryGetValue(id, out string? resolvedId)
            ? resolvedId
            : id;
    }
}
