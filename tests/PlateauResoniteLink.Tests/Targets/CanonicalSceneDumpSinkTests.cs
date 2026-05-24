using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
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
        Justification = "CanonicalSceneDumpSink owns the created inner sink for this test.")]
    public async Task ExecuteAsyncWritesCanonicalDumpAfterInnerSinkCompletes()
    {
        using TemporaryDirectory outputDirectory = new();
        string outputPath = Path.Combine(outputDirectory.Path, "scene.json");
        SceneSinkRecordingClient client = new();
        await using CanonicalSceneDumpSink sink = new(
            new RecordingInnerSceneSink(client),
            client,
            outputPath);

        _ = await sink.ExecuteAsync(
            CreateExecutionPlan(outputDirectory.Path),
            EmptyObjectUnits());

        string dump = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("\"root\"", dump, StringComparison.Ordinal);
        Assert.Contains("\"Building 1\"", dump, StringComparison.Ordinal);
        Assert.EndsWith("\n", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n", dump, StringComparison.Ordinal);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "CanonicalSceneDumpSink owns the created inner sink for this test.")]
    public async Task ExecuteAsyncDoesNotLeaveDumpWhenInnerSinkFailsBeforeCompletion()
    {
        using TemporaryDirectory outputDirectory = new();
        string outputPath = Path.Combine(outputDirectory.Path, "scene.json");
        SceneSinkRecordingClient client = new();
        await using CanonicalSceneDumpSink sink = new(
            new FailingInnerSceneSink(),
            client,
            outputPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.ExecuteAsync(
            CreateExecutionPlan(outputDirectory.Path),
            EmptyObjectUnits()));
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

    private static SceneImportExecutionPlan CreateExecutionPlan(string workDirectory)
    {
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            workDirectory,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0));
        return ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(metadata, workDirectory);
    }

    private static async IAsyncEnumerable<ImportedObjectUnit> EmptyObjectUnits()
    {
        await Task.CompletedTask;
        yield break;
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

    private sealed class RecordingInnerSceneSink(SceneSinkRecordingClient client) : ISceneSink
    {
        public async Task<SceneImportExecutionResult> ExecuteAsync(
            SceneImportExecutionPlan plan,
            IAsyncEnumerable<ImportedObjectUnit> objectUnits,
            CancellationToken cancellationToken = default)
        {
            _ = await client.AddSlotAsync(new AddSlot
            {
                Data = new Slot
                {
                    Parent = new Reference { TargetID = "Root" },
                    Name = new Field_string { Value = "Building 1" },
                },
            }, cancellationToken);
            return new SceneImportExecutionResult(["stub://resonite/location"], 1);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingInnerSceneSink : ISceneSink
    {
        public Task<SceneImportExecutionResult> ExecuteAsync(
            SceneImportExecutionPlan plan,
            IAsyncEnumerable<ImportedObjectUnit> objectUnits,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("inner failed");
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
