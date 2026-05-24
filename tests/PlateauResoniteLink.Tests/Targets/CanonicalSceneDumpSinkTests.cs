using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
        SceneSinkRecordingClient client = new();
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
        Assert.EndsWith("\n", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n", dump, StringComparison.Ordinal);
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
        SceneSinkRecordingClient client = new();
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
    public async Task CreateCanonicalJsonNormalizesImportedTextureUriToSemanticToken()
    {
        using SceneSinkRecordingClient client = new();
        Uri textureUri = await client.ImportTextureAsync(
            new ResoniteRawTextureImport(1, 1, ResoniteTextureColorProfiles.Srgb, [1, 2, 3, 255]),
            CancellationToken.None);
        ResoniteTransportSlotCreationResult slot = await client.AddSlotAsync(new AddSlot
        {
            Data = new Slot
            {
                Parent = new Reference { TargetID = "Root" },
                Name = new Field_string { Value = "Child" },
            },
        }, CancellationToken.None);
        _ = await client.AddComponentAsync(new AddComponent
        {
            ContainerSlotId = slot.Slot.Value,
            Data = new Component
            {
                ComponentType = "FrooxEngine.StaticTexture2D",
                Members = new Dictionary<string, Member>(StringComparer.Ordinal)
                {
                    ["URL"] = new Field_Uri
                    {
                        Value = textureUri,
                    },
                },
            },
        }, CancellationToken.None);

        string dump = SceneSinkRecordingClientCanonicalDump.CreateCanonicalJson(client);

        Assert.Contains("texture:1x1:sRGB:", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("resdb:///texture/0", dump, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateCanonicalJsonPreservesNullUriMemberValue()
    {
        using SceneSinkRecordingClient client = new();
        ResoniteTransportSlotCreationResult slot = await client.AddSlotAsync(new AddSlot
        {
            Data = new Slot
            {
                Parent = new Reference { TargetID = "Root" },
                Name = new Field_string { Value = "Child" },
            },
        }, CancellationToken.None);
        _ = await client.AddComponentAsync(new AddComponent
        {
            ContainerSlotId = slot.Slot.Value,
            Data = new Component
            {
                ComponentType = "FrooxEngine.StaticTexture2D",
                Members = new Dictionary<string, Member>(StringComparer.Ordinal)
                {
                    ["URL"] = new Field_Uri
                    {
                        Value = null,
                    },
                },
            },
        }, CancellationToken.None);

        string dump = SceneSinkRecordingClientCanonicalDump.CreateCanonicalJson(client);

        using JsonDocument document = JsonDocument.Parse(dump);
        JsonElement urlMember = document.RootElement
            .GetProperty("root")
            .GetProperty("children")[0]
            .GetProperty("components")[0]
            .GetProperty("members")
            .GetProperty("URL");
        Assert.Equal("uri", urlMember.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, urlMember.GetProperty("value").ValueKind);
    }

    [Fact]
    public async Task CreateCanonicalJsonEscapesSlotPathReservedCharacters()
    {
        using SceneSinkRecordingClient client = new();
        ResoniteTransportSlotCreationResult slashSlot = await client.AddSlotAsync(new AddSlot
        {
            Data = new Slot
            {
                Parent = new Reference { TargetID = "Root" },
                Name = new Field_string { Value = "A/B#0%1" },
            },
        }, CancellationToken.None);
        _ = await client.AddComponentAsync(new AddComponent
        {
            ContainerSlotId = slashSlot.Slot.Value,
            Data = new Component
            {
                ComponentType = "FrooxEngine.ReferenceHolder",
                Members = new Dictionary<string, Member>(StringComparer.Ordinal)
                {
                    ["Target"] = new Reference { TargetID = slashSlot.Slot.Value },
                },
            },
        }, CancellationToken.None);

        string dump = SceneSinkRecordingClientCanonicalDump.CreateCanonicalJson(client);

        Assert.Contains("A%2FB%230%251", dump, StringComparison.Ordinal);
        Assert.Contains("slot:A%2FB%230%251", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("slot:A/B#0%1", dump, StringComparison.Ordinal);
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

}
