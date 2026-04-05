using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

public sealed class ResoniteLinkSceneBuilder : IResoniteSceneBuilder
{
    private const int MaxQueuedCityObjects = 4;
    private readonly Func<IResoniteLinkClient> clientFactory;
    private readonly Uri endpoint;
    private readonly ITerrainTextureAssetGenerator terrainTextureAssetGenerator;
    private readonly Action<string>? progressReporter;
    private static readonly ResoniteFloat2 DefaultTriplanarTextureScale = new(0.35, 0.35);
    private IResoniteLinkClient? client;
    private ResoniteConstructionMetadata? metadata;
    private string? buildNonce;
    private string? datasetSlotId;
    private string? meshCodeSlotId;
    private string? texturesSlotId;
    private string? materialsSlotId;
    private string? meshCodeMeshesSlotId;
    private Dictionary<string, string>? textureComponentIds;
    private Dictionary<string, string>? materialComponentIds;
    private ResoniteLicenseManager? licenseManager;
    private string? generatedAssetsRoot;
    private HashSet<string>? knownSlotIds;
    private HashSet<string>? knownComponentIds;
    private ConcurrentDictionary<string, Task<string>>? resolvedTexturePathTasks;
    private Channel<QueuedCityObject>? cityObjectChannel;
    private Task? processingTask;
    private int processedCityObjectCount;

    public ResoniteLinkSceneBuilder(Uri endpoint, Action<string>? progressReporter = null)
        : this(endpoint, static () => new ResoniteLinkClient(), new TerrainTextureAssetGenerator(), progressReporter)
    {
    }

    internal ResoniteLinkSceneBuilder(
        Uri endpoint,
        Func<IResoniteLinkClient> clientFactory,
        ITerrainTextureAssetGenerator? terrainTextureAssetGenerator = null,
        Action<string>? progressReporter = null)
    {
        this.endpoint = endpoint;
        this.clientFactory = clientFactory;
        this.terrainTextureAssetGenerator = terrainTextureAssetGenerator ?? new TerrainTextureAssetGenerator();
        this.progressReporter = progressReporter;
    }

    public async Task BeginAsync(
        ResoniteConstructionMetadata metadata,
        string workRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        this.metadata = metadata;
        generatedAssetsRoot = Path.Combine(Path.GetFullPath(workRoot), ".generated-assets");
        buildNonce = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        datasetSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "dataset");
        meshCodeSlotId = ResoniteLinkEntityIdFactory.CreateEntityId(
            metadata.Request.Dataset,
            metadata.Request.MeshCode,
            "meshcode",
            buildNonce);
        string datasetAssetsSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "assets");
        string datasetLicenseComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "license");
        texturesSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "assetgroup",
            "textures");
        string meshesSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "assetgroup",
            "meshes");
        materialsSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "assetgroup",
            "materials");
        meshCodeMeshesSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "meshassets",
            metadata.Request.MeshCode);

        client = clientFactory();
        textureComponentIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        materialComponentIds = new Dictionary<string, string>(StringComparer.Ordinal);
        licenseManager = new ResoniteLicenseManager(metadata.Attribution);
        knownSlotIds = new HashSet<string>(StringComparer.Ordinal);
        knownComponentIds = new HashSet<string>(StringComparer.Ordinal);
        resolvedTexturePathTasks = new ConcurrentDictionary<string, Task<string>>(StringComparer.OrdinalIgnoreCase);
        cityObjectChannel = Channel.CreateBounded<QueuedCityObject>(
            new BoundedChannelOptions(MaxQueuedCityObjects)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
        processedCityObjectCount = 0;
        await client.ConnectAsync(endpoint, cancellationToken);
        processingTask = ProcessQueuedCityObjectsAsync(cityObjectChannel.Reader, cancellationToken);
        ReportProgress(
            $"[live] Connected to {endpoint} for dataset '{metadata.Request.Dataset}' mesh '{metadata.Request.MeshCode}'.");

        await EnsureSlotKnownAsync(
            client,
            datasetSlotId,
            "Root",
            $"PLATEAU {metadata.Request.Dataset}",
            new ResoniteFloat3(0.0, 1.5, 0.0),
            cancellationToken);
        await licenseManager.EnsureDatasetLicenseAsync(client, datasetSlotId, datasetLicenseComponentId, cancellationToken);

        await client.AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    ID = meshCodeSlotId,
                    Parent = new Reference
                    {
                        TargetID = datasetSlotId,
                    },
                    Name = new Field_string
                    {
                        Value = metadata.Request.MeshCode,
                    },
                },
            },
            cancellationToken);
        knownSlotIds.Add(meshCodeSlotId);

        await EnsureSlotKnownAsync(client, datasetAssetsSlotId, datasetSlotId, "Assets", null, cancellationToken);
        await EnsureSlotKnownAsync(client, texturesSlotId, datasetAssetsSlotId, "Textures", null, cancellationToken);
        await EnsureSlotKnownAsync(client, meshesSlotId, datasetAssetsSlotId, "Meshes", null, cancellationToken);
        await EnsureSlotKnownAsync(client, materialsSlotId, datasetAssetsSlotId, "Materials", null, cancellationToken);
        await EnsureSlotKnownAsync(
            client,
            meshCodeMeshesSlotId,
            meshesSlotId,
            metadata.Request.MeshCode,
            null,
            cancellationToken);
        ReportProgress("[live] Dataset slots and asset groups are ready.");
    }

    public async Task ProcessCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        ObjectDisposedException.ThrowIf(client is null, this);
        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(texturesSlotId is null, this);
        ObjectDisposedException.ThrowIf(materialsSlotId is null, this);
        ObjectDisposedException.ThrowIf(meshCodeMeshesSlotId is null, this);
        ObjectDisposedException.ThrowIf(meshCodeSlotId is null, this);
        ObjectDisposedException.ThrowIf(cityObjectChannel is null, this);

        await AwaitProcessingTaskIfCompletedAsync();

        Task<PreparedCityObject> preparationTask = PrepareCityObjectAsync(cityObject, cancellationToken);
        await cityObjectChannel.Writer.WriteAsync(
            new QueuedCityObject(cityObject, preparationTask),
            cancellationToken);
        await AwaitProcessingTaskIfCompletedAsync();
    }

    public async Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(meshCodeSlotId is null, this);
        ObjectDisposedException.ThrowIf(cityObjectChannel is null, this);
        ObjectDisposedException.ThrowIf(processingTask is null, this);

        cityObjectChannel.Writer.TryComplete();
        await processingTask.WaitAsync(cancellationToken);
        ReportProgress($"[live] Completed {processedCityObjectCount} city objects.");
        return [$"{endpoint}#{meshCodeSlotId}"];
    }

    public async ValueTask DisposeAsync()
    {
        cityObjectChannel?.Writer.TryComplete();
        if (processingTask is not null)
        {
            try
            {
                await processingTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        client?.Dispose();
        client = null;
        metadata = null;
        buildNonce = null;
        datasetSlotId = null;
        meshCodeSlotId = null;
        texturesSlotId = null;
        materialsSlotId = null;
        meshCodeMeshesSlotId = null;
        textureComponentIds = null;
        materialComponentIds = null;
        licenseManager = null;
        generatedAssetsRoot = null;
        knownSlotIds = null;
        knownComponentIds = null;
        resolvedTexturePathTasks = null;
        cityObjectChannel = null;
        processingTask = null;
    }

    private async Task ProcessQueuedCityObjectsAsync(
        ChannelReader<QueuedCityObject> reader,
        CancellationToken cancellationToken)
    {
        await foreach (QueuedCityObject queuedCityObject in reader.ReadAllAsync(cancellationToken))
        {
            PreparedCityObject preparedCityObject = await queuedCityObject.PreparationTask.WaitAsync(cancellationToken);
            await ImportPreparedTexturesAsync(preparedCityObject.Textures, cancellationToken);
            await BuildPreparedCityObjectAsync(preparedCityObject, cancellationToken);
            processedCityObjectCount++;
            ReportProgress(
                $"[live] Sent city object {processedCityObjectCount}: "
                + $"{preparedCityObject.CityObject.DisplayName} "
                + $"({preparedCityObject.CityObject.PackageName}/{preparedCityObject.CityObject.SlotKey})");
        }
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }

    private async Task<PreparedCityObject> PrepareCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(generatedAssetsRoot is null, this);
        ObjectDisposedException.ThrowIf(resolvedTexturePathTasks is null, this);

        string datasetRoot = Path.GetFullPath(metadata.Request.LocalSourcePath!);
        (string TexturePath, ResoniteTextureSourceKind TextureSourceKind)[] distinctTextures = cityObject.Materials
            .Where(static material => !string.IsNullOrWhiteSpace(material.TexturePath))
            .Select(static material => (TexturePath: material.TexturePath!, TextureSourceKind: material.TextureSourceKind))
            .Distinct()
            .OrderBy(static texture => texture.TextureSourceKind)
            .ThenBy(static texture => texture.TexturePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Task<PreparedTextureReference>[] texturePreparationTasks = distinctTextures
            .Select(async texture =>
            {
                string absoluteTexturePath = await resolvedTexturePathTasks.GetOrAdd(
                    CreateTextureCacheKey(texture.TexturePath, texture.TextureSourceKind),
                    _ => ResolveTextureImportPathAsync(
                        datasetRoot,
                        texture.TexturePath,
                        texture.TextureSourceKind,
                        cancellationToken));

                return new PreparedTextureReference(
                    texture.TexturePath,
                    texture.TextureSourceKind,
                    absoluteTexturePath);
            })
            .ToArray();
        Task<ImportMeshRawData> meshImportTask = Task.Run(() => CreateMeshImport(cityObject.Mesh), cancellationToken);

        return new PreparedCityObject(
            cityObject,
            await meshImportTask,
            await Task.WhenAll(texturePreparationTasks));
    }

    private async Task ImportPreparedTexturesAsync(
        IReadOnlyList<PreparedTextureReference> texturePaths,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(client is null, this);
        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(texturesSlotId is null, this);
        ObjectDisposedException.ThrowIf(textureComponentIds is null, this);

        foreach (PreparedTextureReference texture in texturePaths)
        {
            string textureCacheKey = CreateTextureCacheKey(texture.TexturePath, texture.TextureSourceKind);
            if (textureComponentIds.ContainsKey(textureCacheKey))
            {
                continue;
            }

            string textureKey = CreatePathKey(textureCacheKey);
            string textureComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
                metadata.Request.Dataset,
                "texture",
                textureKey);
            string textureSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
                metadata.Request.Dataset,
                "textureslot",
                textureKey);

            await EnsureTextureAssetSlotKnownAsync(
                client,
                textureSlotId,
                Path.GetFileName(texture.TexturePath),
                cancellationToken);

            await EnsureAssetComponentUrlKnownAsync(
                client,
                textureSlotId,
                textureComponentId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                "URL",
                () => client.ImportTextureAsync(texture.AbsoluteTexturePath, cancellationToken),
                cancellationToken);

            textureComponentIds[textureCacheKey] = textureComponentId;
        }
    }

    private async Task<string> ResolveTextureImportPathAsync(
        string datasetRoot,
        string texturePath,
        ResoniteTextureSourceKind textureSourceKind,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(generatedAssetsRoot is null, this);

        TerrainTextureOverlay? terrainTextureOverlay = metadata.SourceDataset.TerrainTextureOverlays
            .FirstOrDefault(overlay => string.Equals(overlay.TexturePath, texturePath, StringComparison.Ordinal));
        if (terrainTextureOverlay is not null)
        {
            return await terrainTextureAssetGenerator.EnsureTextureAsync(
                terrainTextureOverlay,
                generatedAssetsRoot,
                cancellationToken);
        }

        return textureSourceKind switch
        {
            ResoniteTextureSourceKind.Dataset => Path.GetFullPath(Path.Combine(datasetRoot, texturePath)),
            ResoniteTextureSourceKind.Bundled => BundledDefaultMaterialAssetStore.GetAbsolutePath(texturePath),
            _ => throw new InvalidOperationException($"Unsupported texture source kind '{textureSourceKind}'."),
        };
    }

    private async Task BuildPreparedCityObjectAsync(
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(client is null, this);
        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(meshCodeSlotId is null, this);
        ObjectDisposedException.ThrowIf(meshCodeMeshesSlotId is null, this);
        ObjectDisposedException.ThrowIf(materialsSlotId is null, this);
        ObjectDisposedException.ThrowIf(textureComponentIds is null, this);
        ObjectDisposedException.ThrowIf(materialComponentIds is null, this);
        ObjectDisposedException.ThrowIf(buildNonce is null, this);

        ResoniteConstructionCityObject cityObject = preparedCityObject.CityObject;
        string cityObjectSlotId = ResoniteLinkEntityIdFactory.CreateEntityId(
            metadata.Request.Dataset,
            metadata.Request.MeshCode,
            "cityobject",
            buildNonce,
            cityObject.SlotKey);
        string meshAssetSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "meshslot",
            $"{metadata.Request.MeshCode}_{cityObject.SlotKey}");
        string staticMeshId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "staticmesh",
            $"{metadata.Request.MeshCode}_{cityObject.SlotKey}");
        string rendererId = ResoniteLinkEntityIdFactory.CreateEntityId(
            metadata.Request.Dataset,
            metadata.Request.MeshCode,
            "renderer",
            buildNonce,
            cityObject.SlotKey);
        string colliderId = ResoniteLinkEntityIdFactory.CreateEntityId(
            metadata.Request.Dataset,
            metadata.Request.MeshCode,
            "collider",
            buildNonce,
            cityObject.SlotKey);

        await client.AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    ID = cityObjectSlotId,
                    Parent = new Reference
                    {
                        TargetID = meshCodeSlotId,
                    },
                    Name = new Field_string
                    {
                        Value = cityObject.DisplayName,
                    },
                    Position = CreateFloat3(cityObject.Transform.Position),
                },
            },
            cancellationToken);
        knownSlotIds!.Add(cityObjectSlotId);

        await EnsureMeshAssetSlotKnownAsync(
            client,
            meshAssetSlotId,
            $"Mesh {cityObject.DisplayName}",
            cancellationToken);

        await EnsureAssetComponentUrlKnownAsync(
            client,
            meshAssetSlotId,
            staticMeshId,
            "[FrooxEngine]FrooxEngine.StaticMesh",
            "URL",
            () => client.ImportMeshAsync(preparedCityObject.MeshImport, cancellationToken),
            cancellationToken);

        List<string> materialIds = [];
        for (int materialIndex = 0; materialIndex < cityObject.Materials.Count; materialIndex++)
        {
            ResoniteMaterialBinding material = cityObject.Materials[materialIndex];
            string materialId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
                metadata.Request.Dataset,
                "materialasset",
                material.MaterialKey);
            string materialSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
                metadata.Request.Dataset,
                "materialslot",
                material.MaterialKey);

            Dictionary<string, Member> materialMembers = CreateMaterialMembers(material);
            string materialComponentType = material.Projection switch
            {
                ResoniteMaterialProjection.Uv => "[FrooxEngine]FrooxEngine.PBS_Metallic",
                ResoniteMaterialProjection.Triplanar => "[FrooxEngine]FrooxEngine.PBS_TriplanarMetallic",
                _ => throw new InvalidOperationException($"Unsupported material projection '{material.Projection}'."),
            };

            string textureCacheKey = CreateTextureCacheKey(material.TexturePath, material.TextureSourceKind);
            if (material.TexturePath is not null
                && textureComponentIds.TryGetValue(textureCacheKey, out string? textureComponentId))
            {
                materialMembers["AlbedoTexture"] = new Reference
                {
                    TargetID = textureComponentId,
                };
            }

            if (!materialComponentIds.ContainsKey(material.MaterialKey))
            {
                await EnsureMaterialAssetSlotKnownAsync(
                    client,
                    materialSlotId,
                    $"Material {materialIndex.ToString(CultureInfo.InvariantCulture)} {material.MaterialKey}",
                    cancellationToken);
                await EnsureComponentKnownAsync(
                    client,
                    materialSlotId,
                    materialId,
                    materialComponentType,
                    materialMembers,
                    cancellationToken);
                materialComponentIds[material.MaterialKey] = materialId;
            }

            materialIds.Add(materialId);
        }

        await client.AddComponentAsync(
            new AddComponent
            {
                ContainerSlotId = cityObjectSlotId,
                Data = new Component
                {
                    ID = rendererId,
                    ComponentType = "[FrooxEngine]FrooxEngine.MeshRenderer",
                    Members = new Dictionary<string, Member>(StringComparer.Ordinal)
                    {
                        ["Mesh"] = new Reference
                        {
                            TargetID = staticMeshId,
                        },
                        ["Materials"] = new SyncList
                        {
                            Elements = materialIds
                                .Select(materialId => (Member)new Reference
                                {
                                    TargetID = materialId,
                                })
                                .ToList(),
                        },
                    },
                },
            },
            cancellationToken);
        knownComponentIds!.Add(rendererId);

        await client.AddComponentAsync(
            new AddComponent
            {
                ContainerSlotId = cityObjectSlotId,
                Data = new Component
                {
                    ID = colliderId,
                    ComponentType = "[FrooxEngine]FrooxEngine.MeshCollider",
                    Members = new Dictionary<string, Member>(StringComparer.Ordinal)
                    {
                        ["Type"] = new Field_Enum
                        {
                            Value = "Static",
                        },
                        ["CharacterCollider"] = new Field_bool
                        {
                            Value = true,
                        },
                        ["Mesh"] = new Reference
                        {
                            TargetID = staticMeshId,
                        },
                    },
                },
            },
            cancellationToken);
        knownComponentIds.Add(colliderId);
    }

    private static ImportMeshRawData CreateMeshImport(ResoniteImportedMesh mesh)
    {
        ResoniteMeshSubmesh[] orderedSubmeshes = mesh.Submeshes
            .OrderBy(static submesh => submesh.Index)
            .ToArray();

        ImportMeshRawData request = new()
        {
            VertexCount = mesh.Vertices.Count,
            HasNormals = true,
            HasTangents = false,
            HasColors = false,
            BoneWeightCount = 0,
            UV_Channel_Dimensions = [2],
            Submeshes = orderedSubmeshes
                .Select(static submesh => (SubmeshRawData)new TriangleSubmeshRawData
                {
                    TriangleCount = submesh.TriangleVertexIndices.Count / 3,
                })
                .ToList(),
            Bones = [],
            BlendShapes = [],
        };

        request.AllocateBuffer();

        for (int index = 0; index < mesh.Vertices.Count; index++)
        {
            ResoniteMeshVertex vertex = mesh.Vertices[index];
            request.Positions[index] = new float3
            {
                x = (float)vertex.Position.X,
                y = (float)vertex.Position.Y,
                z = (float)vertex.Position.Z,
            };
            request.Normals[index] = new float3
            {
                x = (float)vertex.Normal.X,
                y = (float)vertex.Normal.Y,
                z = (float)vertex.Normal.Z,
            };
            request.AccessUV_2D(0)[index] = new float2
            {
                x = (float)vertex.UV0.X,
                y = (float)vertex.UV0.Y,
            };
        }

        for (int submeshIndex = 0; submeshIndex < orderedSubmeshes.Length; submeshIndex++)
        {
            TriangleSubmeshRawData rawSubmesh = (TriangleSubmeshRawData)request.Submeshes[submeshIndex];
            IReadOnlyList<int> indices = orderedSubmeshes[submeshIndex].TriangleVertexIndices;

            for (int index = 0; index < indices.Count; index++)
            {
                rawSubmesh.Indices[index] = indices[index];
            }
        }

        return request;
    }

    private static string CreateTextureCacheKey(string? texturePath, ResoniteTextureSourceKind textureSourceKind)
    {
        return texturePath is null
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"{textureSourceKind}:{texturePath}");
    }

    private static Dictionary<string, Member> CreateMaterialMembers(ResoniteMaterialBinding material)
    {
        Dictionary<string, Member> materialMembers = new(StringComparer.Ordinal)
        {
            ["AlbedoColor"] = new Field_colorX
            {
                Value = new colorX
                {
                    r = (float)material.BaseColor.R,
                    g = (float)material.BaseColor.G,
                    b = (float)material.BaseColor.B,
                    a = (float)material.BaseColor.A,
                    Profile = "sRGB",
                },
            },
            ["Smoothness"] = new Field_float
            {
                Value = 0.0f,
            },
        };

        if (material.Projection == ResoniteMaterialProjection.Triplanar)
        {
            materialMembers["Metallic"] = new Field_float
            {
                Value = 0.0f,
            };
            materialMembers["TextureScale"] = new Field_float2
            {
                Value = new float2
                {
                    x = (float)DefaultTriplanarTextureScale.X,
                    y = (float)DefaultTriplanarTextureScale.Y,
                },
            };
            materialMembers["TextureOffset"] = new Field_float2
            {
                Value = new float2
                {
                    x = 0.0f,
                    y = 0.0f,
                },
            };
            materialMembers["TriplanarBlendPower"] = new Field_float
            {
                Value = 8.0f,
            };
            materialMembers["ObjectSpace"] = new Field_bool
            {
                Value = true,
            };
        }

        if (material.DepthOffset is not null)
        {
            materialMembers["OffsetFactor"] = new Field_float
            {
                Value = (float)material.DepthOffset.Factor,
            };
            materialMembers["OffsetUnits"] = new Field_float
            {
                Value = (float)material.DepthOffset.Units,
            };
        }

        return materialMembers;
    }

    private static Field_float3 CreateFloat3(ResoniteFloat3 value)
    {
        return new Field_float3
        {
            Value = new float3
            {
                x = (float)value.X,
                y = (float)value.Y,
                z = (float)value.Z,
            },
        };
    }

    private async Task AwaitProcessingTaskIfCompletedAsync()
    {
        if (processingTask is not null && processingTask.IsCompleted)
        {
            await processingTask;
        }
    }

    private async Task EnsureSlotKnownAsync(
        IResoniteLinkClient client,
        string slotId,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(knownSlotIds is null, this);

        if (knownSlotIds.Contains(slotId))
        {
            return;
        }

        await EnsureSlotAsync(client, slotId, parentId, slotName, position, cancellationToken);
        knownSlotIds.Add(slotId);
    }

    private async Task EnsureTextureAssetSlotKnownAsync(
        IResoniteLinkClient client,
        string slotId,
        string slotName,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(texturesSlotId is null, this);
        await EnsureAssetSlotKnownAsync(client, slotId, texturesSlotId, slotName, cancellationToken);
    }

    private async Task EnsureMaterialAssetSlotKnownAsync(
        IResoniteLinkClient client,
        string slotId,
        string slotName,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(materialsSlotId is null, this);
        await EnsureAssetSlotKnownAsync(client, slotId, materialsSlotId, slotName, cancellationToken);
    }

    private async Task EnsureMeshAssetSlotKnownAsync(
        IResoniteLinkClient client,
        string slotId,
        string slotName,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(meshCodeMeshesSlotId is null, this);
        await EnsureAssetSlotKnownAsync(client, slotId, meshCodeMeshesSlotId, slotName, cancellationToken);
    }

    private async Task EnsureAssetSlotKnownAsync(
        IResoniteLinkClient client,
        string slotId,
        string parentId,
        string slotName,
        CancellationToken cancellationToken)
    {
        await EnsureSlotKnownAsync(client, slotId, parentId, slotName, null, cancellationToken);
    }

    private async Task<Uri> EnsureAssetComponentUrlKnownAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentId,
        string componentType,
        string uriMemberName,
        Func<Task<Uri>> importAssetAsync,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(knownComponentIds is null, this);

        Uri assetUri = await EnsureAssetComponentUrlAsync(
            client,
            containerSlotId,
            componentId,
            componentType,
            uriMemberName,
            importAssetAsync,
            cancellationToken);
        knownComponentIds.Add(componentId);
        return assetUri;
    }

    private async Task EnsureComponentKnownAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(knownComponentIds is null, this);

        if (knownComponentIds.Contains(componentId))
        {
            return;
        }

        await EnsureComponentAsync(
            client,
            containerSlotId,
            componentId,
            componentType,
            members,
            cancellationToken);
        knownComponentIds.Add(componentId);
    }

    private static async Task EnsureSlotAsync(
        IResoniteLinkClient client,
        string slotId,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        CancellationToken cancellationToken)
    {
        Slot? existingSlot = await client.GetSlotAsync(slotId, cancellationToken);
        if (existingSlot is not null)
        {
            return;
        }

        await client.AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    ID = slotId,
                    Parent = new Reference
                    {
                        TargetID = parentId,
                    },
                    Name = new Field_string
                    {
                        Value = slotName,
                    },
                    Position = position is null ? null : CreateFloat3(position),
                },
            },
            cancellationToken);
    }

    private static async Task<Uri> EnsureAssetComponentUrlAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentId,
        string componentType,
        string uriMemberName,
        Func<Task<Uri>> importAssetAsync,
        CancellationToken cancellationToken)
    {
        Component? existingComponent = await client.GetComponentAsync(componentId, cancellationToken);
        if (TryGetUri(existingComponent, uriMemberName) is Uri existingUri)
        {
            return existingUri;
        }

        Uri assetUri = await importAssetAsync();
        Dictionary<string, Member> members = new(StringComparer.Ordinal)
        {
            [uriMemberName] = new Field_Uri
            {
                Value = assetUri,
            },
        };

        if (existingComponent is null)
        {
            await client.AddComponentAsync(
                new AddComponent
                {
                    ContainerSlotId = containerSlotId,
                    Data = new Component
                    {
                        ID = componentId,
                        ComponentType = componentType,
                        Members = members,
                    },
                },
                cancellationToken);
        }
        else
        {
            await client.UpdateComponentAsync(
                new UpdateComponent
                {
                    Data = new Component
                    {
                        ID = componentId,
                        Members = members,
                    },
                },
                cancellationToken);
        }

        return assetUri;
    }

    private static async Task EnsureComponentAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        CancellationToken cancellationToken)
    {
        Component? existingComponent = await client.GetComponentAsync(componentId, cancellationToken);
        if (existingComponent is not null)
        {
            return;
        }

        await client.AddComponentAsync(
            new AddComponent
            {
                ContainerSlotId = containerSlotId,
                Data = new Component
                {
                    ID = componentId,
                    ComponentType = componentType,
                    Members = new Dictionary<string, Member>(members, StringComparer.Ordinal),
                },
            },
            cancellationToken);
    }

    private static Uri? TryGetUri(Component? component, string memberName)
    {
        if (component is null
            || !component.Members.TryGetValue(memberName, out Member? member)
            || member is not Field_Uri fieldUri)
        {
            return null;
        }

        return fieldUri.Value;
    }

    private static string CreatePathKey(string path)
    {
        return string.Concat(path.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
    }

    private sealed record QueuedCityObject(
        ResoniteConstructionCityObject CityObject,
        Task<PreparedCityObject> PreparationTask);

    private sealed record PreparedCityObject(
        ResoniteConstructionCityObject CityObject,
        ImportMeshRawData MeshImport,
        IReadOnlyList<PreparedTextureReference> Textures);

    private sealed record PreparedTextureReference(
        string TexturePath,
        ResoniteTextureSourceKind TextureSourceKind,
        string AbsoluteTexturePath);
}
