using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Diagnostics;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

using static PlateauResoniteLink.Tests.TextureImportSourceTestFactory;

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
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "CanonicalSceneDumpSink owns the created inner sink for this test.")]
    public async Task ExecuteAsyncIncludesTerrainGridCoverageSummaryForRealDataRegressionDumps()
    {
        using TemporaryDirectory outputDirectory = new();
        string outputPath = Path.Combine(outputDirectory.Path, "scene.json");
        SceneSinkRecordingClient client = new();
        await using CanonicalSceneDumpSink sink = new(
            new ConsumingInnerSceneSink(),
            client,
            outputPath);

        TerrainGridGeometry grid = new(
            Width: 2,
            Height: 2,
            Size: new Float2(10.0, 10.0),
            MinHeight: -2.0,
            MaxHeight: 20.0,
            HeightSamples: [20.0, 10.0, 0.0, -2.0],
            SampleCoverage:
            [
                TerrainGridSampleCoverage.Measured,
                TerrainGridSampleCoverage.Measured,
                TerrainGridSampleCoverage.NoSurface,
                TerrainGridSampleCoverage.NoSurface,
            ]);
        ImportedCityObject cityObject = new(
            ObjectKey: "dem:53391458",
            DisplayName: "DEM 53391458",
            PackageName: "dem",
            ActualMeshCode: "53391458",
            LodLevel: null,
            Transform: new Transform3D(new Float3(100.0, 25.0, 200.0)),
            Geometry: grid,
            Materials: [],
            SourceFileRelativePath: "udx/dem/533914_dem.gml",
            SourceFileRootMeshCode: "533914");

        _ = await sink.ExecuteAsync(
            CreateExecutionPlan(outputDirectory.Path),
            CreateObjectUnits(new ImportedObjectUnit(
                "udx/dem/533914_dem.gml",
                "dem",
                null,
                [cityObject],
                matchedMeshCode: "533914")));

        string dump = await File.ReadAllTextAsync(outputPath);
        using JsonDocument document = JsonDocument.Parse(dump);
        JsonElement objectNode = document.RootElement.GetProperty("objects")[0];
        JsonElement summary = objectNode.GetProperty("terrainGridSummary");
        Assert.Equal("53391458", objectNode.GetProperty("actualMeshCode").GetString());
        Assert.Equal("terrainGrid", objectNode.GetProperty("geometryKind").GetString());
        Assert.Equal(2, summary.GetProperty("measured").GetProperty("sampleCount").GetInt32());
        Assert.Equal(2, summary.GetProperty("noSurface").GetProperty("sampleCount").GetInt32());
        Assert.Equal(2, summary.GetProperty("edgeNoSurfaceSampleCount").GetInt32());
        Assert.Equal("25", summary.GetProperty("verticalOriginWorldHeight").GetString());
        Assert.False(summary.TryGetProperty("gridLocalHeightOffset", out _));
        Assert.Equal("-1", summary.GetProperty("displacementMagnitude").GetString());
        Assert.Equal(4, summary.GetProperty("sampleWorldVertexSummary").GetProperty("vertexCount").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(summary.GetProperty("sampleWorldVertexSummary").GetProperty("worldVertexSha256").GetString()));
        Assert.Equal("45", summary.GetProperty("measured").GetProperty("worldHeightMax").GetString());
        Assert.Equal("23", summary.GetProperty("noSurface").GetProperty("worldHeightMin").GetString());
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "CanonicalSceneDumpSink owns the created inner sink for this test.")]
    public async Task ExecuteAsyncIncludesAllWorldVertexHashesForStaticDynamicRegressionDumps()
    {
        using TemporaryDirectory outputDirectory = new();
        string outputPath = Path.Combine(outputDirectory.Path, "scene.json");
        SceneSinkRecordingClient client = new();
        await using CanonicalSceneDumpSink sink = new(
            new ConsumingInnerSceneSink(),
            client,
            outputPath);

        ImportedMesh staticMesh = CreateTriangleMesh(
            [
                new Float3(1.0, 2.0, 3.0),
                new Float3(4.0, 5.0, 6.0),
                new Float3(7.0, 8.0, 9.0),
            ]);
        ImportedMesh dynamicLocalMesh = CreateTriangleMesh(
            [
                new Float3(0.0, 4.0, 2.0),
                new Float3(3.0, 7.0, 5.0),
                new Float3(6.0, 10.0, 8.0),
            ]);
        TerrainGridGeometry grid = new(
            Width: 2,
            Height: 2,
            Size: new Float2(2.0, 2.0),
            MinHeight: 0.0,
            MaxHeight: 0.0,
            HeightSamples: [0.0, 0.0, 0.0, 0.0],
            SampleCoverage:
            [
                TerrainGridSampleCoverage.Measured,
                TerrainGridSampleCoverage.Measured,
                TerrainGridSampleCoverage.Measured,
                TerrainGridSampleCoverage.Measured,
            ]);
        ImportedCityObject staticObject = CreateDumpCityObject(
            "static",
            new Transform3D(new Float3(10.0, 20.0, 30.0)),
            new TriangleMeshGeometry(staticMesh));
        ImportedCityObject dynamicObject = CreateDumpCityObject(
            "dynamic",
            new Transform3D(new Float3(11.0, 18.0, 31.0)),
            new DynamicTerrainGeometry(new TriangleMeshGeometry(dynamicLocalMesh), grid));

        _ = await sink.ExecuteAsync(
            CreateExecutionPlan(outputDirectory.Path),
            CreateObjectUnits(new ImportedObjectUnit(
                "udx/dem/533914_dem.gml",
                "dem",
                null,
                [dynamicObject, staticObject],
                matchedMeshCode: "533914")));

        string dump = await File.ReadAllTextAsync(outputPath);
        using JsonDocument document = JsonDocument.Parse(dump);
        JsonElement dynamicVertexSummary = document.RootElement.GetProperty("objects")[0].GetProperty("triangleMeshWorldVertexSummary");
        JsonElement staticVertexSummary = document.RootElement.GetProperty("objects")[1].GetProperty("triangleMeshWorldVertexSummary");
        Assert.Equal(3, dynamicVertexSummary.GetProperty("vertexCount").GetInt32());
        Assert.Equal(
            staticVertexSummary.GetProperty("worldVertexSha256").GetString(),
            dynamicVertexSummary.GetProperty("worldVertexSha256").GetString());
        Assert.Equal(
            staticVertexSummary.GetProperty("bounds").GetRawText(),
            dynamicVertexSummary.GetProperty("bounds").GetRawText());
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "CanonicalSceneDumpSink owns the created inner sink for this test.")]
    public async Task ExecuteAsyncReportsNoSurfaceGridSamplesThatStillHaveStaticMeshFootprint()
    {
        using TemporaryDirectory outputDirectory = new();
        string outputPath = Path.Combine(outputDirectory.Path, "scene.json");
        SceneSinkRecordingClient client = new();
        await using CanonicalSceneDumpSink sink = new(
            new ConsumingInnerSceneSink(),
            client,
            outputPath);

        ImportedMesh staticMesh = CreateTriangleMesh(
            [
                new Float3(-1.0, 0.0, -1.0),
                new Float3(0.0, 0.0, -1.0),
                new Float3(-1.0, 0.0, 0.0),
            ]);
        TerrainGridGeometry grid = new(
            Width: 3,
            Height: 3,
            Size: new Float2(2.0, 2.0),
            MinHeight: 0.0,
            MaxHeight: 0.0,
            HeightSamples: [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
            SampleCoverage:
            [
                TerrainGridSampleCoverage.NoSurface,
                TerrainGridSampleCoverage.Measured,
                TerrainGridSampleCoverage.Measured,
                TerrainGridSampleCoverage.Measured,
                TerrainGridSampleCoverage.Measured,
                TerrainGridSampleCoverage.Measured,
                TerrainGridSampleCoverage.Measured,
                TerrainGridSampleCoverage.Measured,
                TerrainGridSampleCoverage.NoSurface,
            ]);
        ImportedCityObject cityObject = CreateDumpCityObject(
            "dynamic",
            new Transform3D(new Float3(10.0, 20.0, 30.0)),
            new DynamicTerrainGeometry(new TriangleMeshGeometry(staticMesh), grid));

        _ = await sink.ExecuteAsync(
            CreateExecutionPlan(outputDirectory.Path),
            CreateObjectUnits(new ImportedObjectUnit(
                "udx/dem/533914_dem.gml",
                "dem",
                null,
                [cityObject],
                matchedMeshCode: "533914")));

        string dump = await File.ReadAllTextAsync(outputPath);
        using JsonDocument document = JsonDocument.Parse(dump);
        JsonElement summary = document.RootElement.GetProperty("objects")[0].GetProperty("terrainGridStaticMeshFootprintSummary");
        Assert.Equal(1, summary.GetProperty("staticMeshTriangleCount").GetInt32());
        Assert.Equal(2, summary.GetProperty("noSurfaceSampleCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("noSurfaceWithinStaticMeshFootprintSampleCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("edgeNoSurfaceWithinStaticMeshFootprintSampleCount").GetInt32());
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
            CreateRawTextureSource(1, 1, ResoniteTextureColorProfiles.Srgb, [1, 2, 3, 255]),
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
    public async Task CreateCanonicalJsonIncludesMeshBoundsForVertexRegressionDumps()
    {
        using SceneSinkRecordingClient client = new();
        Uri meshUri = await client.ImportMeshAsync(
            ResoniteMeshImportFactory.Create(new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(-2.0, 3.0, 4.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(0.25, 0.75)),
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(5.0, -6.0, 7.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(0.5, 0.125)),
                    new ResoniteMeshVertex(
                        new ResoniteFloat3(1.0, 2.0, -8.0),
                        new ResoniteFloat3(0.0, 1.0, 0.0),
                        new ResoniteFloat2(0.875, 0.625)),
                ],
                [new ResoniteMeshSubmesh(0, [0, 1, 2])])),
            CancellationToken.None);

        string dump = SceneSinkRecordingClientCanonicalDump.CreateCanonicalJson(client);

        using JsonDocument document = JsonDocument.Parse(dump);
        JsonElement mesh = document.RootElement.GetProperty("imports").GetProperty("meshes")[0];
        Assert.Equal("resdb:///mesh/0", meshUri.ToString());
        Assert.Equal("-2", mesh.GetProperty("positionBounds").GetProperty("min").GetProperty("x").GetString());
        Assert.Equal("-8", mesh.GetProperty("positionBounds").GetProperty("min").GetProperty("z").GetString());
        Assert.Equal("7", mesh.GetProperty("positionBounds").GetProperty("size").GetProperty("x").GetString());
        Assert.Equal("0.25", mesh.GetProperty("uvBounds").GetProperty("min").GetProperty("x").GetString());
        Assert.Equal("0.875", mesh.GetProperty("uvBounds").GetProperty("max").GetProperty("x").GetString());
        Assert.Equal("0.625", mesh.GetProperty("uvBounds").GetProperty("size").GetProperty("x").GetString());
    }

    [Fact]
    public async Task CreateCanonicalJsonIncludesHeightMapBoundarySummary()
    {
        using SceneSinkRecordingClient client = new();
        Uri textureUri = await client.ImportTextureAsync(
            TextureImportSourceFactory.CreateGeneratedRgbaFloat32Image(
                cancellationToken =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(new RgbaFloat32RawTexturePayload(
                        2,
                        2,
                        colorProfile: null,
                        CreateRgbaFloat32Bytes(
                            blueValues:
                            [
                                0.0f,
                                1.5f,
                                3.0f,
                                0.0f,
                            ])));
                },
                "heightmap-test",
                colorProfile: null),
            CancellationToken.None);

        string dump = SceneSinkRecordingClientCanonicalDump.CreateCanonicalJson(client);

        using JsonDocument document = JsonDocument.Parse(dump);
        JsonElement hdrTexture = document.RootElement.GetProperty("imports").GetProperty("hdrTextures")[0];
        Assert.StartsWith("resdb:///texture/", textureUri.ToString(), StringComparison.Ordinal);
        Assert.Equal("0", hdrTexture.GetProperty("heightMapSummary").GetProperty("blueMin").GetString());
        Assert.Equal("3", hdrTexture.GetProperty("heightMapSummary").GetProperty("blueMax").GetString());
        Assert.Equal(4, hdrTexture.GetProperty("heightMapSummary").GetProperty("edgePixelCount").GetInt32());
        Assert.Equal(2, hdrTexture.GetProperty("heightMapSummary").GetProperty("edgeZeroLikePixelCount").GetInt32());
        Assert.Equal(1, hdrTexture.GetProperty("heightMapSummary").GetProperty("maxLikePixelCount").GetInt32());
        Assert.Equal(1, hdrTexture.GetProperty("heightMapSummary").GetProperty("edgeMaxLikePixelCount").GetInt32());
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

    private static ImportedCityObject CreateDumpCityObject(
        string objectKey,
        Transform3D transform,
        ConstructionGeometry geometry)
    {
        return new ImportedCityObject(
            ObjectKey: objectKey,
            DisplayName: objectKey,
            PackageName: "dem",
            ActualMeshCode: "53391458",
            LodLevel: null,
            Transform: transform,
            Geometry: geometry,
            Materials: [],
            SourceFileRelativePath: "udx/dem/533914_dem.gml",
            SourceFileRootMeshCode: "533914");
    }

    private static ImportedMesh CreateTriangleMesh(IReadOnlyList<Float3> positions)
    {
        return new ImportedMesh(
            positions
                .Select(static position => new MeshVertex(
                    position,
                    new Float3(0.0, 1.0, 0.0),
                    new Float2(0.0, 0.0)))
                .ToArray(),
            [new MeshSubmesh(0, [0, 1, 2])]);
    }

    private static async IAsyncEnumerable<ImportedObjectUnit> EmptyObjectUnits()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<ImportedObjectUnit> CreateObjectUnits(params ImportedObjectUnit[] objectUnits)
    {
        await Task.CompletedTask;
        foreach (ImportedObjectUnit objectUnit in objectUnits)
        {
            yield return objectUnit;
        }
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

    private sealed class ConsumingInnerSceneSink : ISceneSink
    {
        public async Task<SceneImportExecutionResult> ExecuteAsync(
            SceneImportExecutionPlan plan,
            IAsyncEnumerable<ImportedObjectUnit> objectUnits,
            CancellationToken cancellationToken = default)
        {
            int count = 0;
            await foreach (ImportedObjectUnit objectUnit in objectUnits.WithCancellation(cancellationToken))
            {
                count += objectUnit.CityObjects.Count;
            }

            return new SceneImportExecutionResult(["stub://resonite/location"], count);
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

    private static byte[] CreateRgbaFloat32Bytes(IReadOnlyList<float> blueValues)
    {
        byte[] bytes = new byte[blueValues.Count * 4 * sizeof(float)];
        for (int index = 0; index < blueValues.Count; index++)
        {
            float[] channels = [0.0f, 0.0f, blueValues[index], 1.0f];
            Buffer.BlockCopy(channels, 0, bytes, index * 4 * sizeof(float), 4 * sizeof(float));
        }

        return bytes;
    }
}
