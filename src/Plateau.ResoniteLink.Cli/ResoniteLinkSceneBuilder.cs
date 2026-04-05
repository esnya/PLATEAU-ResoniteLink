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
    private const string SharedAssetsSlotName = "Shared";
    private const string SharedMaterialsSlotName = "Materials";
    private const float DefaultNormalScale = 1.0f;
    private const float DefaultBundledHeightScale = 0.002f;
    private readonly Func<IResoniteLinkClient> clientFactory;
    private readonly Uri endpoint;
    private readonly ITerrainTextureAssetGenerator terrainTextureAssetGenerator;
    private readonly Action<string>? progressReporter;
    private static readonly ResoniteFloat2 DefaultTriplanarTextureScale = BundledDefaultMaterialTiling.DefaultTilesPerMeter;
    private const float DefaultWireframeThickness = 0.01f;
    private const double DefaultWireframeFillOpacity = 0.08;
    private IResoniteLinkClient? client;
    private ResoniteConstructionMetadata? metadata;
    private string? buildNonce;
    private string? datasetSlotId;
    private string? meshCodeSlotId;
    private string? datasetAssetsSlotId;
    private string? sharedAssetsSlotId;
    private string? sharedMaterialsSlotId;
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
        datasetAssetsSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "assets");
        string datasetLicenseComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "license");
        sharedAssetsSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "assetgroup",
            "shared");
        sharedMaterialsSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "assetgroup",
            "shared-materials");

        client = clientFactory();
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
        await EnsureSlotKnownAsync(client, sharedAssetsSlotId, datasetAssetsSlotId, SharedAssetsSlotName, null, cancellationToken);
        await EnsureSlotKnownAsync(client, sharedMaterialsSlotId, sharedAssetsSlotId, SharedMaterialsSlotName, null, cancellationToken);
        ReportProgress("[live] Dataset slots and asset groups are ready.");
    }

    public async Task ProcessCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        ObjectDisposedException.ThrowIf(client is null, this);
        ObjectDisposedException.ThrowIf(metadata is null, this);
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
        datasetAssetsSlotId = null;
        sharedAssetsSlotId = null;
        sharedMaterialsSlotId = null;
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
        ObjectDisposedException.ThrowIf(datasetAssetsSlotId is null, this);
        ObjectDisposedException.ThrowIf(sharedMaterialsSlotId is null, this);
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
        string packageSlotId = GetMeshCodePackageSlotId(
            metadata.Request.Dataset,
            metadata.Request.MeshCode,
            buildNonce,
            cityObject.PackageName);
        string lodSlotId = GetMeshCodeLodSlotId(
            metadata.Request.Dataset,
            metadata.Request.MeshCode,
            buildNonce,
            cityObject.PackageName,
            cityObject.LodLevel);
        string assetPackageSlotId = GetAssetPackageSlotId(metadata.Request.Dataset, cityObject.PackageName);
        string assetLodSlotId = GetAssetLodSlotId(metadata.Request.Dataset, cityObject.PackageName, cityObject.LodLevel);
        string assetCityObjectSlotId = GetAssetCityObjectSlotId(
            metadata.Request.Dataset,
            metadata.Request.MeshCode,
            cityObject.SlotKey);

        await EnsureSlotKnownAsync(client, packageSlotId, meshCodeSlotId, cityObject.PackageName, null, cancellationToken);
        await EnsureSlotKnownAsync(client, lodSlotId, packageSlotId, FormatLodSlotName(cityObject.LodLevel), null, cancellationToken);

        await client.AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    ID = cityObjectSlotId,
                    Parent = new Reference
                    {
                        TargetID = lodSlotId,
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

        await EnsureSlotKnownAsync(client, assetPackageSlotId, datasetAssetsSlotId, cityObject.PackageName, null, cancellationToken);
        await EnsureSlotKnownAsync(client, assetLodSlotId, assetPackageSlotId, FormatLodSlotName(cityObject.LodLevel), null, cancellationToken);
        await EnsureSlotKnownAsync(client, assetCityObjectSlotId, assetLodSlotId, cityObject.DisplayName, null, cancellationToken);

        await EnsureMeshAssetSlotKnownAsync(
            client,
            meshAssetSlotId,
            cityObject.DisplayName,
            assetCityObjectSlotId,
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
            string textureComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
                metadata.Request.Dataset,
                "texture",
                material.MaterialKey);
            string emissionTextureComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
                metadata.Request.Dataset,
                "texture",
                $"{material.MaterialKey}-emission");
            string heightTextureComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
                metadata.Request.Dataset,
                "texture",
                $"{material.MaterialKey}-height");
            string metallicTextureComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
                metadata.Request.Dataset,
                "texture",
                $"{material.MaterialKey}-metallic");
            string normalTextureComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
                metadata.Request.Dataset,
                "texture",
                $"{material.MaterialKey}-normal");

            Dictionary<string, Member> materialMembers = CreateMaterialMembers(material);
            string materialComponentType = material.MaterialType switch
            {
                ResoniteMaterialType.Standard => material.Projection switch
                {
                    ResoniteMaterialProjection.Uv => "[FrooxEngine]FrooxEngine.PBS_Metallic",
                    ResoniteMaterialProjection.Triplanar => "[FrooxEngine]FrooxEngine.PBS_TriplanarMetallic",
                    _ => throw new InvalidOperationException($"Unsupported material projection '{material.Projection}'."),
                },
                ResoniteMaterialType.Wireframe => "[FrooxEngine]FrooxEngine.WireframeMaterial",
                _ => throw new InvalidOperationException($"Unsupported material type '{material.MaterialType}'."),
            };

            if (!materialComponentIds.ContainsKey(material.MaterialKey))
            {
                await EnsureMaterialAssetSlotKnownAsync(
                    client,
                    materialSlotId,
                    material.MaterialKey,
                    cancellationToken);

                if (material.TexturePath is not null
                    && preparedCityObject.TryGetTexturePath(material.TexturePath, material.TextureSourceKind, out string? absoluteTexturePath))
                {
                    await EnsureAssetComponentUrlKnownAsync(
                        client,
                        materialSlotId,
                        textureComponentId,
                        "[FrooxEngine]FrooxEngine.StaticTexture2D",
                        "URL",
                        () => client.ImportTextureAsync(absoluteTexturePath!, cancellationToken),
                        cancellationToken);
                    materialMembers["AlbedoTexture"] = new Reference
                    {
                        TargetID = textureComponentId,
                    };
                }

                if (TryGetBundledCompanionTextureSet(material, out BundledDefaultMaterialTextureSet? textureSet)
                    && textureSet is not null)
                {
                    if (textureSet.NormalPath is not null)
                    {
                        await EnsureAssetComponentUrlKnownAsync(
                            client,
                            materialSlotId,
                            normalTextureComponentId,
                            "[FrooxEngine]FrooxEngine.StaticTexture2D",
                            "URL",
                            () => client.ImportTextureAsync(textureSet.NormalPath, cancellationToken),
                            cancellationToken);
                        materialMembers["NormalMap"] = new Reference
                        {
                            TargetID = normalTextureComponentId,
                        };
                        materialMembers["NormalScale"] = new Field_float
                        {
                            Value = DefaultNormalScale,
                        };
                    }

                    if (textureSet.HeightPath is not null
                        && material.Projection == ResoniteMaterialProjection.Uv)
                    {
                        await EnsureAssetComponentUrlKnownAsync(
                            client,
                            materialSlotId,
                            heightTextureComponentId,
                            "[FrooxEngine]FrooxEngine.StaticTexture2D",
                            "URL",
                            () => client.ImportTextureAsync(textureSet.HeightPath, cancellationToken),
                            cancellationToken);
                        materialMembers["HeightMap"] = new Reference
                        {
                            TargetID = heightTextureComponentId,
                        };
                        materialMembers["HeightScale"] = new Field_float
                        {
                            Value = DefaultBundledHeightScale,
                        };
                    }

                    if (textureSet.MetallicPath is not null)
                    {
                        await EnsureAssetComponentUrlKnownAsync(
                            client,
                            materialSlotId,
                            metallicTextureComponentId,
                            "[FrooxEngine]FrooxEngine.StaticTexture2D",
                            "URL",
                            () => client.ImportTextureAsync(textureSet.MetallicPath, cancellationToken),
                            cancellationToken);
                        Reference metallicReference = new()
                        {
                            TargetID = metallicTextureComponentId,
                        };
                        materialMembers["MetallicMap"] = metallicReference;
                        materialMembers["OcclusionMap"] = new Reference
                        {
                            TargetID = metallicTextureComponentId,
                        };
                    }

                    if (textureSet.EmissionPath is not null)
                    {
                        await EnsureAssetComponentUrlKnownAsync(
                            client,
                            materialSlotId,
                            emissionTextureComponentId,
                            "[FrooxEngine]FrooxEngine.StaticTexture2D",
                            "URL",
                            () => client.ImportTextureAsync(textureSet.EmissionPath, cancellationToken),
                            cancellationToken);
                        materialMembers["EmissiveMap"] = new Reference
                        {
                            TargetID = emissionTextureComponentId,
                        };
                        materialMembers["EmissiveColor"] = CreateColorMember(new ResoniteColor(1.0, 1.0, 1.0, 1.0));
                    }
                }

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
        Dictionary<string, Member> materialMembers = new(StringComparer.Ordinal);

        if (material.MaterialType == ResoniteMaterialType.Standard)
        {
            materialMembers["AlbedoColor"] = CreateColorMember(material.BaseColor);
            materialMembers["Smoothness"] = new Field_float
            {
                Value = 0.0f,
            };
        }

        if (material.MaterialType == ResoniteMaterialType.Standard
            && material.TextureScale is not null)
        {
            materialMembers["TextureScale"] = new Field_float2
            {
                Value = new float2
                {
                    x = (float)material.TextureScale.X,
                    y = (float)material.TextureScale.Y,
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
        }

        if (material.MaterialType == ResoniteMaterialType.Standard
            && material.Projection == ResoniteMaterialProjection.Triplanar)
        {
            materialMembers["Metallic"] = new Field_float
            {
                Value = 0.0f,
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

        if (material.MaterialType == ResoniteMaterialType.Wireframe)
        {
            materialMembers["Thickness"] = new Field_float
            {
                Value = DefaultWireframeThickness,
            };
            materialMembers["ScreenSpace"] = new Field_bool
            {
                Value = true,
            };
            materialMembers["LineColor"] = CreateColorMember(material.BaseColor);
            materialMembers["FillColor"] = CreateColorMember(material.BaseColor with
            {
                A = Math.Clamp(material.BaseColor.A * DefaultWireframeFillOpacity, 0.0, 1.0),
            });
            materialMembers["DoubleSided"] = new Field_bool
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

    private static bool TryGetBundledCompanionTextureSet(
        ResoniteMaterialBinding material,
        out BundledDefaultMaterialTextureSet? textureSet)
    {
        textureSet = null;
        if (material.MaterialType != ResoniteMaterialType.Standard
            || material.TextureSourceKind != ResoniteTextureSourceKind.Bundled
            || string.IsNullOrWhiteSpace(material.TexturePath))
        {
            return false;
        }

        string albedoLogicalPath = material.TexturePath;
        string stem = Path.GetFileNameWithoutExtension(albedoLogicalPath);
        if (!stem.EndsWith("_Color", StringComparison.Ordinal))
        {
            return false;
        }

        string directory = Path.GetDirectoryName(albedoLogicalPath)?.Replace('\\', '/')
            ?? throw new InvalidOperationException($"Could not determine bundled texture directory for '{albedoLogicalPath}'.");
        string baseStem = stem[..^"_Color".Length];

        textureSet = new BundledDefaultMaterialTextureSet(
            TryResolveBundledTexture(directory, $"{baseStem}_Emission.jpg"),
            TryResolveBundledTexture(directory, $"{baseStem}_Height.jpg"),
            TryResolveBundledTexture(directory, $"{baseStem}_Metallic.png"),
            TryResolveBundledTexture(directory, $"{baseStem}_NormalGL.jpg"));
        return true;
    }

    private static string? TryResolveBundledTexture(string directory, string fileName)
    {
        string logicalPath = $"{directory}/{fileName}";
        return BundledDefaultMaterialAssetStore.TryGetAbsolutePath(logicalPath, out string absolutePath)
            ? absolutePath
            : null;
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

    private static Field_colorX CreateColorMember(ResoniteColor color)
    {
        return new Field_colorX
        {
            Value = new colorX
            {
                r = (float)color.R,
                g = (float)color.G,
                b = (float)color.B,
                a = (float)color.A,
                Profile = "sRGB",
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

    private async Task EnsureMaterialAssetSlotKnownAsync(
        IResoniteLinkClient client,
        string slotId,
        string slotName,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(sharedMaterialsSlotId is null, this);
        await EnsureAssetSlotKnownAsync(client, slotId, sharedMaterialsSlotId, slotName, cancellationToken);
    }

    private async Task EnsureMeshAssetSlotKnownAsync(
        IResoniteLinkClient client,
        string slotId,
        string slotName,
        string parentId,
        CancellationToken cancellationToken)
    {
        await EnsureAssetSlotKnownAsync(client, slotId, parentId, slotName, cancellationToken);
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

    private static string FormatLodSlotName(int? lodLevel)
    {
        return lodLevel.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"LOD{lodLevel.Value}")
            : "LOD0";
    }

    private static string GetMeshCodePackageSlotId(string dataset, string meshCode, string nonce, string packageName)
    {
        return ResoniteLinkEntityIdFactory.CreateEntityId(dataset, meshCode, "package", nonce, packageName);
    }

    private static string GetMeshCodeLodSlotId(string dataset, string meshCode, string nonce, string packageName, int? lodLevel)
    {
        return ResoniteLinkEntityIdFactory.CreateEntityId(dataset, meshCode, "lod", nonce, packageName, FormatLodSlotName(lodLevel));
    }

    private static string GetAssetPackageSlotId(string dataset, string packageName)
    {
        return ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(dataset, "assetpackage", packageName);
    }

    private static string GetAssetLodSlotId(string dataset, string packageName, int? lodLevel)
    {
        return ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(dataset, "assetlod", packageName, FormatLodSlotName(lodLevel));
    }

    private static string GetAssetCityObjectSlotId(string dataset, string meshCode, string slotKey)
    {
        return ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(dataset, "assetcityobject", meshCode, slotKey);
    }

    private sealed record QueuedCityObject(
        ResoniteConstructionCityObject CityObject,
        Task<PreparedCityObject> PreparationTask);

    private sealed record PreparedCityObject(
        ResoniteConstructionCityObject CityObject,
        ImportMeshRawData MeshImport,
        IReadOnlyList<PreparedTextureReference> Textures)
    {
        public bool TryGetTexturePath(
            string texturePath,
            ResoniteTextureSourceKind textureSourceKind,
            out string? absoluteTexturePath)
        {
            PreparedTextureReference? preparedTexture = Textures.FirstOrDefault(texture =>
                string.Equals(texture.TexturePath, texturePath, StringComparison.Ordinal)
                && texture.TextureSourceKind == textureSourceKind);
            absoluteTexturePath = preparedTexture?.AbsoluteTexturePath;
            return preparedTexture is not null;
        }
    }

    private sealed record BundledDefaultMaterialTextureSet(
        string? EmissionPath,
        string? HeightPath,
        string? MetallicPath,
        string? NormalPath);

    private sealed record PreparedTextureReference(
        string TexturePath,
        ResoniteTextureSourceKind TextureSourceKind,
        string AbsoluteTexturePath);
}
