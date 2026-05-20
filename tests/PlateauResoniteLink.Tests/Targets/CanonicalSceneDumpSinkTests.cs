using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Diagnostics;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class CanonicalSceneDumpSinkTests
{
    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "CanonicalSceneDumpSink owns the created target for this test.")]
    public async Task ExecuteAsyncWritesCanonicalDumpAfterFakeLinkImportCompletes()
    {
        using TemporaryDirectory outputDirectory = new();
        string outputPath = Path.Combine(outputDirectory.Path, "scene.json");
        using SceneSinkRecordingClient client = new();
        await using CanonicalSceneDumpSink sink = new(
            ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(
                client,
                new RecordingTerrainTextureAssetGenerator(CreateDeterministicTerrainTexture)),
            client,
            outputPath);

        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            outputDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525_bldg.gml"]);
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: "building-1",
            DisplayName: "Building 1",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
            Materials:
            [
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            CollisionEnabled: true,
            SourceFileRelativePath: "udx/bldg/53394525_bldg.gml");

        _ = await sink.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, outputDirectory.Path),
            ResoniteLiveSceneImportTargetTestSupport.CreateImportedObjectUnitsForTestsAsync([cityObject]));

        string dump = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("\"root\"", dump, StringComparison.Ordinal);
        Assert.Contains("\"imports\"", dump, StringComparison.Ordinal);
        Assert.Contains("\"Building 1\"", dump, StringComparison.Ordinal);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "CanonicalSceneDumpSink owns the created target for this test.")]
    public async Task ExecuteAsyncDoesNotLeaveDumpWhenImportFailsBeforeCompletion()
    {
        using TemporaryDirectory outputDirectory = new();
        string outputPath = Path.Combine(outputDirectory.Path, "scene.json");
        using SceneSinkRecordingClient client = new();
        await using CanonicalSceneDumpSink sink = new(
            ResoniteLiveSceneImportTargetTestSupport.CreateImportTarget(
                client,
                session: new DelegatingClientSession(
                    client,
                    (_, _) => throw new InvalidOperationException("connection failed"))),
            client,
            outputPath);
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            outputDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.ExecuteAsync(
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, outputDirectory.Path),
            ResoniteLiveSceneImportTargetTestSupport.CreateImportedObjectUnitsForTestsAsync([])));
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task CreateCanonicalJsonIsIndependentFromBatchOrIndividualMutationApplication()
    {
        using SceneSinkRecordingClient batchedClient = new();
        using SceneSinkRecordingClient individualClient = new();
        AddSlot childSlot = new()
        {
            Data = new Slot
            {
                ID = "local_slot",
                Parent = new Reference { TargetID = "Root" },
                Name = new Field_string { Value = "Child" },
            },
        };
        AddComponent childComponent = new()
        {
            ContainerSlotId = "local_slot",
            Data = new Component
            {
                ID = "local_component",
                ComponentType = "FrooxEngine.StaticTexture2D",
                Members = new Dictionary<string, Member>(StringComparer.Ordinal)
                {
                    ["URL"] = new Field_Uri
                    {
                        Value = new Uri("resdb:///texture/0", UriKind.Absolute),
                    },
                },
            },
        };
        ResoniteRawTextureImport firstTexture = new(1, 1, ResoniteTextureColorProfiles.Srgb, [1, 2, 3, 255]);
        ResoniteRawHdrTextureImport hdrTexture = new(1, 1, [1, 0, 0, 0, 2, 0, 0, 0]);
        ResoniteRawTextureImport secondTexture = new(1, 1, ResoniteTextureColorProfiles.Srgb, [4, 5, 6, 255]);

        _ = await batchedClient.ImportTextureAsync(firstTexture, CancellationToken.None);
        _ = await batchedClient.ImportTextureAsync(hdrTexture, CancellationToken.None);
        Uri batchedTextureUri = await batchedClient.ImportTextureAsync(secondTexture, CancellationToken.None);
        childComponent.Data.Members["URL"] = new Field_Uri { Value = batchedTextureUri };
        _ = await batchedClient.RunDataModelOperationBatchAsync([childSlot, childComponent], CancellationToken.None);

        _ = await individualClient.ImportTextureAsync(firstTexture, CancellationToken.None);
        _ = await individualClient.ImportTextureAsync(hdrTexture, CancellationToken.None);
        Uri individualTextureUri = await individualClient.ImportTextureAsync(secondTexture, CancellationToken.None);
        ResoniteTransportSlotCreationResult individualSlot = await individualClient.AddSlotAsync(new AddSlot
        {
            Data = new Slot
            {
                Parent = new Reference { TargetID = "Root" },
                Name = new Field_string { Value = "Child" },
            },
        }, CancellationToken.None);
        _ = await individualClient.AddComponentAsync(new AddComponent
        {
            ContainerSlotId = individualSlot.Slot.Value,
            Data = new Component
            {
                ComponentType = "FrooxEngine.StaticTexture2D",
                Members = new Dictionary<string, Member>(StringComparer.Ordinal)
                {
                    ["URL"] = new Field_Uri
                    {
                        Value = individualTextureUri,
                    },
                },
            },
        }, CancellationToken.None);

        Assert.Equal(
            SceneSinkRecordingClientCanonicalDump.CreateCanonicalJson(individualClient),
            SceneSinkRecordingClientCanonicalDump.CreateCanonicalJson(batchedClient));
    }

    [Fact]
    public async Task CreateCanonicalJsonSortsDictionaryMembersAsObjects()
    {
        using SceneSinkRecordingClient firstClient = new();
        using SceneSinkRecordingClient secondClient = new();

        await AddDictionaryBackedComponentAsync(firstClient, "First", "Second");
        await AddDictionaryBackedComponentAsync(secondClient, "Second", "First");

        string firstDump = SceneSinkRecordingClientCanonicalDump.CreateCanonicalJson(firstClient);
        string secondDump = SceneSinkRecordingClientCanonicalDump.CreateCanonicalJson(secondClient);
        Assert.Equal(firstDump, secondDump);
        Assert.Contains("\"First\"", firstDump, StringComparison.Ordinal);
        Assert.Contains("\"Second\"", firstDump, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Key\"", firstDump, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateCanonicalJsonDisambiguatesDuplicateSiblingSlotReferenceTargets()
    {
        using SceneSinkRecordingClient targetFirstClient = new();
        using SceneSinkRecordingClient holderFirstClient = new();

        await AddDuplicateSiblingReferenceAsync(targetFirstClient, createTargetFirst: true);
        await AddDuplicateSiblingReferenceAsync(holderFirstClient, createTargetFirst: false);

        string targetFirstDump = SceneSinkRecordingClientCanonicalDump.CreateCanonicalJson(targetFirstClient);
        string holderFirstDump = SceneSinkRecordingClientCanonicalDump.CreateCanonicalJson(holderFirstClient);
        Assert.Equal(targetFirstDump, holderFirstDump);
        Assert.Contains("slot:Duplicate#0", targetFirstDump, StringComparison.Ordinal);
        Assert.Contains("\"path\": \"Duplicate#1\"", targetFirstDump, StringComparison.Ordinal);
    }

    private static GeneratedTerrainTexture CreateDeterministicTerrainTexture(TerrainTextureOverlay overlay)
    {
        return new GeneratedTerrainTexture(
            new ResoniteRawTextureImport(
                2,
                2,
                ResoniteTextureColorProfiles.Srgb,
                [128, 160, 192, 255, 128, 160, 192, 255, 128, 160, 192, 255, 128, 160, 192, 255]),
            new ResoniteFloat2(1.0, 1.0),
            new ResoniteFloat2(0.0, 0.0),
            overlay.GetRequiredPrimaryTileSource());
    }

    private static async Task AddDictionaryBackedComponentAsync(
        SceneSinkRecordingClient client,
        string firstMemberName,
        string secondMemberName)
    {
        ResoniteTransportSlotCreationResult slot = await client.AddSlotAsync(new AddSlot
        {
            Data = new Slot
            {
                Parent = new Reference { TargetID = "Root" },
                Name = new Field_string { Value = "Child" },
            },
        }, CancellationToken.None);
        Dictionary<string, Member> nestedMembers = new(StringComparer.Ordinal)
        {
            [firstMemberName] = new Field_string { Value = firstMemberName },
            [secondMemberName] = new Field_string { Value = secondMemberName },
        };

        _ = await client.AddComponentAsync(new AddComponent
        {
            ContainerSlotId = slot.Slot.Value,
            Data = new Component
            {
                ComponentType = "FrooxEngine.DictionaryBackedComponent",
                Members = new Dictionary<string, Member>(StringComparer.Ordinal)
                {
                    ["Nested"] = new SyncList
                    {
                        Elements =
                        [
                            new SyncObject
                            {
                                Members = nestedMembers,
                            },
                        ],
                    },
                },
            },
        }, CancellationToken.None);
    }

    private static async Task AddDuplicateSiblingReferenceAsync(
        SceneSinkRecordingClient client,
        bool createTargetFirst)
    {
        if (createTargetFirst)
        {
            ResoniteTransportSlotCreationResult targetSlot = await AddNamedSlotAsync(client, "Duplicate");
            ResoniteTransportSlotCreationResult holderSlot = await AddNamedSlotAsync(client, "Duplicate");
            await AddSlotReferenceComponentAsync(client, holderSlot.Slot.Value, targetSlot.Slot.Value);
            return;
        }

        ResoniteTransportSlotCreationResult holder = await AddNamedSlotAsync(client, "Duplicate");
        ResoniteTransportSlotCreationResult target = await AddNamedSlotAsync(client, "Duplicate");
        await AddSlotReferenceComponentAsync(client, holder.Slot.Value, target.Slot.Value);
    }

    private static Task<ResoniteTransportSlotCreationResult> AddNamedSlotAsync(
        SceneSinkRecordingClient client,
        string name)
    {
        return client.AddSlotAsync(new AddSlot
        {
            Data = new Slot
            {
                Parent = new Reference { TargetID = "Root" },
                Name = new Field_string { Value = name },
            },
        }, CancellationToken.None);
    }

    private static async Task AddSlotReferenceComponentAsync(
        SceneSinkRecordingClient client,
        string holderSlotId,
        string targetSlotId)
    {
        _ = await client.AddComponentAsync(new AddComponent
        {
            ContainerSlotId = holderSlotId,
            Data = new Component
            {
                ComponentType = "FrooxEngine.ReferenceHolder",
                Members = new Dictionary<string, Member>(StringComparer.Ordinal)
                {
                    ["Target"] = new Reference { TargetID = targetSlotId },
                },
            },
        }, CancellationToken.None);
    }
}
