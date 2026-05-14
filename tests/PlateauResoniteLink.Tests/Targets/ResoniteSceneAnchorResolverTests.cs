using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

using TransportComponentLocator = PlateauResoniteLink.Transport.ResoniteLink.ResoniteTransportComponentLocator;
using TransportSlotLocator = PlateauResoniteLink.Transport.ResoniteLink.ResoniteTransportSlotLocator;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteSceneAnchorResolverTests
{
    [Fact]
    public void SnapshotCanResolveNestedAssetsCommonReuseWithoutAnId()
    {
        Slot datasetRoot = CreateSlot(
            "dataset-root",
            "PLATEAU tokyo23ku",
            children:
            [
                CreateSlot(
                    "assets-root",
                    "Assets",
                    "dataset-root",
                    children:
                    [
                        CreateSlot(
                            id: null,
                            "Common",
                            "assets-root"),
                    ]),
            ]);

        ResoniteSceneSlotSnapshot snapshot = new(datasetRoot);
        ResoniteSceneChildLookupResult lookup = snapshot.GetUniqueDescendantLookupResult(
            "dataset-root",
            "Assets",
            "Common");

        Assert.Equal(ResoniteSceneChildLookupState.FoundWithoutId, lookup.State);
        Assert.NotNull(lookup.Slot);
        Assert.Null(lookup.Slot!.ID);
        Assert.Equal("Common", lookup.Slot!.Name!.Value);
    }

    [Fact]
    public async Task ResolveAsyncUsesCompletionSourceFileRootPositionWhenPresent()
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
                        CreateSlot("source-root", "plateau_tokyo23ku_bldg_53394525", datasetRootSlotId, new ResoniteFloat3(1.0, 0.0, 2.0)),
                    ]);
            }

            return null;
        });

        ResoniteSceneAnchorResolver resolver = new();

        SceneAnchor anchor = await resolver.ResolveAsync(
            client,
            new ResoniteSlotLocator(datasetRootSlotId),
            completionMeshCode,
            CancellationToken.None);

        Assert.Equal(new ResoniteSlotLocator("source-root"), anchor.LocationSlot);
        Assert.Equal(completionMeshCode, anchor.MeshCode);
        Assert.Equal(new ResoniteSlotLocator("source-root"), anchor.ReferenceSourceFileRoot);
        Assert.Equal(1.0, anchor.Position.X, 4);
        Assert.Equal(0.0, anchor.Position.Y, 4);
        Assert.Equal(2.0, anchor.Position.Z, 4);
    }

    [Fact]
    public async Task ResolveAsyncUsesPositionedReferenceSourceFileRootForFallbackAnchorPosition()
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
                        CreateSlot("source-root-b", "plateau_tokyo23ku_bldg_53394526", datasetRootSlotId, largerMeshPosition),
                        CreateSlot("source-root-a", "plateau_tokyo23ku_bldg_53394525", datasetRootSlotId, smallerMeshPosition),
                    ]);
            }

            return null;
        });

        ResoniteSceneAnchorResolver resolver = new();

        SceneAnchor anchor = await resolver.ResolveAsync(
            client,
            new ResoniteSlotLocator(datasetRootSlotId),
            completionMeshCode,
            CancellationToken.None);

        ResoniteFloat3 expectedPosition = ComputeExpectedAnchorPosition(
            "53394525",
            completionMeshCode,
            smallerMeshPosition);
        Assert.Equal(expectedPosition.X, anchor.Position.X, 4);
        Assert.Equal(expectedPosition.Y, anchor.Position.Y, 4);
        Assert.Equal(expectedPosition.Z, anchor.Position.Z, 4);
        Assert.Equal(new ResoniteSlotLocator("source-root-a"), anchor.LocationSlot);
        Assert.Equal(new ResoniteSlotLocator("source-root-a"), anchor.ReferenceSourceFileRoot);
    }

    [Fact]
    public async Task ResolveAsyncFallsBackToZeroWhenCompletionSourceFileRootPositionIsMissing()
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
                        CreateSlot("source-root", "plateau_tokyo23ku_bldg_53394525", datasetRootSlotId),
                    ]);
            }

            return null;
        });

        ResoniteSceneAnchorResolver resolver = new();

        SceneAnchor anchor = await resolver.ResolveAsync(
            client,
            new ResoniteSlotLocator(datasetRootSlotId),
            completionMeshCode,
            CancellationToken.None);

        Assert.Equal(new ResoniteSlotLocator("source-root"), anchor.LocationSlot);
        Assert.Equal(new ResoniteSlotLocator("source-root"), anchor.ReferenceSourceFileRoot);
        Assert.Equal(0.0, anchor.Position.X, 4);
        Assert.Equal(0.0, anchor.Position.Y, 4);
        Assert.Equal(0.0, anchor.Position.Z, 4);
    }

    [Fact]
    public async Task ResolveAsyncReturnsDatasetRootAnchorWhenOnlyAssetsExist()
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
                        CreateSlot(
                            "assets-root",
                            "Assets",
                            datasetRootSlotId,
                            children:
                            [
                                CreateSlot("common-root", "Common", "assets-root"),
                            ]),
                    ]);
            }

            return null;
        });

        ResoniteSceneAnchorResolver resolver = new();

        SceneAnchor anchor = await resolver.ResolveAsync(
            client,
            new ResoniteSlotLocator(datasetRootSlotId),
            completionMeshCode,
            CancellationToken.None);

        Assert.Equal(new ResoniteSlotLocator(datasetRootSlotId), anchor.LocationSlot);
        Assert.Equal(completionMeshCode, anchor.MeshCode);
        Assert.Null(anchor.ReferenceSourceFileRoot);
        Assert.Equal(0.0, anchor.Position.X, 4);
        Assert.Equal(0.0, anchor.Position.Y, 4);
        Assert.Equal(0.0, anchor.Position.Z, 4);
    }

    [Fact]
    public void SnapshotDoesNotSelectChildWhenDuplicateNamesExist()
    {
        Slot datasetRoot = CreateSlot(
            "dataset-root",
            "PLATEAU tokyo23ku",
            children:
            [
                CreateSlot("mesh-b", "plateau_tokyo23ku_bldg_53394525_a", "dataset-root"),
                CreateSlot("mesh-a", "plateau_tokyo23ku_bldg_53394525_a", "dataset-root"),
            ]);

        ResoniteSceneSlotSnapshot snapshot = new(datasetRoot);

        ResoniteSceneChildLookupResult lookup = snapshot.GetUniqueChildLookupResult("plateau_tokyo23ku_bldg_53394525_a", "dataset-root");

        Assert.Equal(ResoniteSceneChildLookupState.FoundWithoutId, lookup.State);
        Assert.Null(lookup.Slot);
    }

    private static ResoniteFloat3 ComputeExpectedAnchorPosition(
        string referenceMeshCode,
        string completionMeshCode,
        ResoniteFloat3 referencePosition)
    {
        Assert.True(PlateauMeshCode.TryGetGeodeticCenter(referenceMeshCode, out GeodeticCoordinate referenceCenter));
        Assert.True(PlateauMeshCode.TryGetGeodeticCenter(completionMeshCode, out GeodeticCoordinate completionCenter));

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

        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<ResoniteTransportComponentCreationResult> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ResoniteTransportSlotCreationResult> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operations.Count > 0)
            {
                throw new NotSupportedException();
            }

            return Task.FromResult(
                new BatchResponse
                {
                    Success = true,
                    Responses = [],
                });
        }

        public Task<Component?> GetComponentAsync(TransportComponentLocator component, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<Slot?> GetSlotAsync(TransportSlotLocator slot, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string SlotId, int Depth) key = (slot.Value, depth);
            int callCount = getSlotCallCounts.TryGetValue(key, out int existingCount)
                ? existingCount + 1
                : 1;
            getSlotCallCounts[key] = callCount;
            Slot? resolvedSlot = getSlot(slot.Value, depth, callCount);
            return Task.FromResult(resolvedSlot is null ? null : CloneSlot(resolvedSlot, depth: 8));
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task UpdateComponentAsync(ResoniteComponentUpdate request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
