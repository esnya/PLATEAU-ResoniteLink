using GeographicLib;

using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ResoniteSceneAnchorResolverTests
{
    [Fact]
    public async Task ResolveAsyncContinuesPollingWhenMatchingChildExistsWithoutIdUntilIdSurfaces()
    {
        const string datasetRootSlotId = "dataset-root";
        const string completionMeshCode = "53394525";
        const string anchorSlotId = "anchor-slot";
        ResoniteFloat3 expectedPosition = new(12.5, 0.0, 34.5);
        using AnchorResolverFakeClient client = new AnchorResolverFakeClient((slotId, depth, callCount) =>
        {
            if (string.Equals(slotId, datasetRootSlotId, StringComparison.Ordinal) && depth == 0)
            {
                return CreateSlot(datasetRootSlotId, "PLATEAU tokyo23ku");
            }

            if (string.Equals(slotId, datasetRootSlotId, StringComparison.Ordinal) && depth == 1)
            {
                return callCount == 1
                    ? CreateSlot(
                        datasetRootSlotId,
                        "PLATEAU tokyo23ku",
                        children:
                        [
                            CreateSlot(id: null, completionMeshCode, datasetRootSlotId, expectedPosition),
                        ])
                    : CreateSlot(
                        datasetRootSlotId,
                        "PLATEAU tokyo23ku",
                        children:
                        [
                            CreateSlot(anchorSlotId, completionMeshCode, datasetRootSlotId, expectedPosition),
                        ]);
            }

            if (string.Equals(slotId, anchorSlotId, StringComparison.Ordinal))
            {
                return CreateSlot(anchorSlotId, completionMeshCode, datasetRootSlotId, expectedPosition);
            }

            return null;
        });

        ResoniteSceneAnchorResolver resolver = new();

        SceneAnchor anchor = await resolver.ResolveAsync(
            client,
            datasetRootSlotId,
            completionMeshCode,
            datasetRootExisted: true,
            CancellationToken.None);

        Assert.Equal(anchorSlotId, anchor.SlotId);
        Assert.Equal(expectedPosition, anchor.Position);
        Assert.Empty(client.AddedSlots);
        Assert.Equal(2, client.GetSlotCallCount(datasetRootSlotId, 1));
    }

    [Fact]
    public async Task ResolveAsyncDoesNotCreateDuplicateAnchorWhenFinalSnapshotStillLacksChildId()
    {
        const string datasetRootSlotId = "dataset-root";
        const string completionMeshCode = "53394525";
        using AnchorResolverFakeClient client = new AnchorResolverFakeClient((slotId, depth, callCount) =>
        {
            if (string.Equals(slotId, datasetRootSlotId, StringComparison.Ordinal) && depth == 0)
            {
                return CreateSlot(datasetRootSlotId, "PLATEAU tokyo23ku");
            }

            if (string.Equals(slotId, datasetRootSlotId, StringComparison.Ordinal) && depth == 1)
            {
                return CreateSlot(
                    datasetRootSlotId,
                    "PLATEAU tokyo23ku",
                    children:
                    [
                        CreateSlot(id: null, completionMeshCode, datasetRootSlotId, new ResoniteFloat3(1.0, 0.0, 2.0)),
                    ]);
            }

            return null;
        });

        ResoniteSceneAnchorResolver resolver = new();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(
            client,
            datasetRootSlotId,
            completionMeshCode,
            datasetRootExisted: false,
            CancellationToken.None));

        Assert.Contains("did not surface an ID", exception.Message, StringComparison.Ordinal);
        Assert.Empty(client.AddedSlots);
    }

    [Fact]
    public async Task ResolveAsyncUsesDeterministicReferenceMeshRootForFallbackAnchorPosition()
    {
        const string datasetRootSlotId = "dataset-root";
        const string completionMeshCode = "53394527";
        ResoniteFloat3 largerMeshPosition = new(100.0, 0.0, 200.0);
        ResoniteFloat3 smallerMeshPosition = new(10.0, 0.0, 20.0);
        using AnchorResolverFakeClient client = new AnchorResolverFakeClient((slotId, depth, callCount) =>
        {
            if (string.Equals(slotId, datasetRootSlotId, StringComparison.Ordinal) && depth == 0)
            {
                return CreateSlot(datasetRootSlotId, "PLATEAU tokyo23ku");
            }

            if (string.Equals(slotId, datasetRootSlotId, StringComparison.Ordinal) && depth == 1)
            {
                return CreateSlot(
                    datasetRootSlotId,
                    "PLATEAU tokyo23ku",
                    children:
                    [
                        CreateSlot("mesh-b", "53394526", datasetRootSlotId, largerMeshPosition),
                        CreateSlot("mesh-a", "53394525", datasetRootSlotId, smallerMeshPosition),
                    ]);
            }

            return null;
        });

        ResoniteSceneAnchorResolver resolver = new();

        SceneAnchor anchor = await resolver.ResolveAsync(
            client,
            datasetRootSlotId,
            completionMeshCode,
            datasetRootExisted: false,
            CancellationToken.None);

        ResoniteFloat3 expectedPosition = ComputeExpectedAnchorPosition(
            "53394525",
            completionMeshCode,
            smallerMeshPosition);
        Assert.Equal(expectedPosition.X, anchor.Position.X, 4);
        Assert.Equal(expectedPosition.Y, anchor.Position.Y, 4);
        Assert.Equal(expectedPosition.Z, anchor.Position.Z, 4);
        AddSlot createdAnchor = Assert.Single(client.AddedSlots);
        Field_float3 position = Assert.IsType<Field_float3>(createdAnchor.Data.Position);
        Assert.Equal(expectedPosition.X, position.Value.x, 3);
        Assert.Equal(expectedPosition.Y, position.Value.y, 3);
        Assert.Equal(expectedPosition.Z, position.Value.z, 3);
    }

    private static ResoniteFloat3 ComputeExpectedAnchorPosition(
        string referenceMeshCode,
        string completionMeshCode,
        ResoniteFloat3 referencePosition)
    {
        Assert.True(PlateauMeshCode.TryGetCenter(referenceMeshCode, out ResoniteLocalOrigin referenceCenter));
        Assert.True(PlateauMeshCode.TryGetCenter(completionMeshCode, out ResoniteLocalOrigin completionCenter));

        LocalCartesian cartesian = new(
            referenceCenter.Latitude,
            referenceCenter.Longitude,
            referenceCenter.Altitude,
            Geocentric.WGS84);
        (double x, double y, double z) eun = cartesian.Forward(
            completionCenter.Latitude,
            completionCenter.Longitude,
            completionCenter.Altitude);
        return new ResoniteFloat3(
            referencePosition.X + eun.x,
            referencePosition.Y,
            referencePosition.Z + eun.y);
    }

    private static Slot CreateSlot(
        string? id,
        string name,
        string? parentId = null,
        ResoniteFloat3? position = null,
        IReadOnlyList<Slot>? children = null)
    {
        return new Slot
        {
            ID = id,
            Parent = parentId is null ? null : new Reference
            {
                TargetID = parentId,
            },
            Name = new Field_string
            {
                Value = name,
            },
            Position = position is null ? null : new Field_float3
            {
                Value = new float3
                {
                    x = (float)position.X,
                    y = (float)position.Y,
                    z = (float)position.Z,
                },
            },
            Children = children?.Select(static child => CloneSlot(child, depth: 8)).ToList(),
        };
    }

    private static Slot CloneSlot(Slot source, int depth)
    {
        Slot clone = new()
        {
            ID = source.ID,
            Parent = source.Parent is null ? null : new Reference
            {
                TargetID = source.Parent.TargetID,
            },
            Name = source.Name is null ? null : new Field_string
            {
                Value = source.Name.Value,
            },
            Position = source.Position is Field_float3 position ? new Field_float3
            {
                Value = new float3
                {
                    x = position.Value.x,
                    y = position.Value.y,
                    z = position.Value.z,
                },
            } : null,
        };

        if (depth > 0 && source.Children is not null)
        {
            clone.Children = source.Children
                .Select(child => CloneSlot(child, depth - 1))
                .ToList();
        }

        return clone;
    }

    private sealed class AnchorResolverFakeClient(Func<string, int, int, Slot?> getSlot)
        : IResoniteLinkClient
    {
        private readonly Dictionary<(string SlotId, int Depth), int> getSlotCallCounts = new();
        private int nextSlotId;

        public List<AddSlot> AddedSlots { get; } = [];

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
            throw new NotSupportedException();
        }

        public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string slotId = string.IsNullOrWhiteSpace(request.Data.ID)
                ? $"created-slot-{Interlocked.Increment(ref nextSlotId)}"
                : request.Data.ID;
            request.Data.ID = slotId;
            AddedSlots.Add(request);
            return Task.FromResult(slotId);
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string SlotId, int Depth) key = (slotId, depth);
            int callCount = getSlotCallCounts.TryGetValue(key, out int existingCount)
                ? existingCount + 1
                : 1;
            getSlotCallCounts[key] = callCount;
            Slot? slot = getSlot(slotId, depth, callCount);
            return Task.FromResult(slot is null ? null : CloneSlot(slot, depth: 8));
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public int GetSlotCallCount(string slotId, int depth)
        {
            return getSlotCallCounts.TryGetValue((slotId, depth), out int count) ? count : 0;
        }
    }
}
