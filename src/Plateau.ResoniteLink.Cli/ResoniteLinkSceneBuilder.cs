using System.Globalization;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

public sealed class ResoniteLinkSceneBuilder : IResoniteSceneBuilder
{
    private readonly Func<IResoniteLinkClient> clientFactory;
    private readonly Uri endpoint;
    private IResoniteLinkClient? client;
    private ResoniteConstructionMetadata? metadata;
    private string? buildNonce;
    private string? datasetSlotId;
    private string? meshCodeSlotId;
    private string? texturesSlotId;
    private string? materialsSlotId;
    private string? meshCodeMeshesSlotId;
    private Dictionary<string, string>? textureComponentIds;

    public ResoniteLinkSceneBuilder(Uri endpoint)
        : this(endpoint, static () => new ResoniteLinkClient())
    {
    }

    internal ResoniteLinkSceneBuilder(Uri endpoint, Func<IResoniteLinkClient> clientFactory)
    {
        this.endpoint = endpoint;
        this.clientFactory = clientFactory;
    }

    public async Task BeginAsync(
        ResoniteConstructionMetadata metadata,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        this.metadata = metadata;
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
        await client.ConnectAsync(endpoint, cancellationToken);

        await EnsureSlotAsync(
            client,
            datasetSlotId,
            "Root",
            $"PLATEAU {metadata.Request.Dataset}",
            new ResoniteFloat3(0.0, 1.5, 0.0),
            cancellationToken);

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
                        Value = $"Mesh Code {metadata.Request.MeshCode}",
                    },
                },
            },
            cancellationToken);

        await EnsureSlotAsync(client, datasetAssetsSlotId, datasetSlotId, "Assets", null, cancellationToken);
        await EnsureSlotAsync(client, texturesSlotId, datasetAssetsSlotId, "Textures", null, cancellationToken);
        await EnsureSlotAsync(client, meshesSlotId, datasetAssetsSlotId, "Meshes", null, cancellationToken);
        await EnsureSlotAsync(client, materialsSlotId, datasetAssetsSlotId, "Materials", null, cancellationToken);
        await EnsureSlotAsync(
            client,
            meshCodeMeshesSlotId,
            meshesSlotId,
            $"Mesh Code {metadata.Request.MeshCode}",
            null,
            cancellationToken);
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
        ObjectDisposedException.ThrowIf(textureComponentIds is null, this);
        ObjectDisposedException.ThrowIf(buildNonce is null, this);

        await ImportTexturesAsync(
            client,
            metadata,
            cityObject,
            texturesSlotId,
            textureComponentIds,
            cancellationToken);

        await BuildCityObjectAsync(
            client,
            metadata,
            cityObject,
            meshCodeSlotId,
            meshCodeMeshesSlotId,
            materialsSlotId,
            textureComponentIds,
            buildNonce,
            cancellationToken);
    }

    public Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(meshCodeSlotId is null, this);
        return Task.FromResult<IReadOnlyList<string>>([$"{endpoint}#{meshCodeSlotId}"]);
    }

    public ValueTask DisposeAsync()
    {
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
        return ValueTask.CompletedTask;
    }

    private static async Task ImportTexturesAsync(
        IResoniteLinkClient client,
        ResoniteConstructionMetadata metadata,
        ResoniteConstructionCityObject cityObject,
        string texturesSlotId,
        Dictionary<string, string> textureComponentIds,
        CancellationToken cancellationToken)
    {
        string datasetRoot = Path.GetFullPath(metadata.Request.InputPath!);
        string[] texturePaths = cityObject.Materials
            .Select(static material => material.TexturePath)
            .Where(static texturePath => !string.IsNullOrWhiteSpace(texturePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static texturePath => texturePath, StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();

        foreach (string texturePath in texturePaths)
        {
            string absoluteTexturePath = Path.GetFullPath(Path.Combine(datasetRoot, texturePath));
            string textureKey = CreatePathKey(texturePath);
            string textureComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
                metadata.Request.Dataset,
                "texture",
                textureKey);
            string textureSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
                metadata.Request.Dataset,
                "textureslot",
                textureKey);

            await EnsureSlotAsync(
                client,
                textureSlotId,
                texturesSlotId,
                Path.GetFileName(texturePath),
                null,
                cancellationToken);

            await EnsureAssetComponentUrlAsync(
                client,
                textureSlotId,
                textureComponentId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                "URL",
                () => client.ImportTextureAsync(absoluteTexturePath, cancellationToken),
                cancellationToken);

            textureComponentIds[texturePath] = textureComponentId;
        }
    }

    private static async Task BuildCityObjectAsync(
        IResoniteLinkClient client,
        ResoniteConstructionMetadata metadata,
        ResoniteConstructionCityObject cityObject,
        string meshCodeSlotId,
        string meshCodeMeshesSlotId,
        string materialsSlotId,
        Dictionary<string, string> textureComponentIds,
        string buildNonce,
        CancellationToken cancellationToken)
    {
        string cityObjectSlotId = ResoniteLinkEntityIdFactory.CreateEntityId(
            metadata.Request.Dataset,
            metadata.Request.MeshCode,
            "cityobject",
            buildNonce,
            cityObject.SlotKey);
        string cityObjectAssetsSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            metadata.Request.Dataset,
            "cityobjectassets",
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

        await EnsureSlotAsync(
            client,
            cityObjectAssetsSlotId,
            meshCodeMeshesSlotId,
            $"{cityObject.DisplayName} Assets",
            null,
            cancellationToken);

        await EnsureAssetComponentUrlAsync(
            client,
            cityObjectAssetsSlotId,
            staticMeshId,
            "[FrooxEngine]FrooxEngine.StaticMesh",
            "URL",
            () => client.ImportMeshAsync(CreateMeshImport(cityObject.Mesh), cancellationToken),
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

            if (material.TexturePath is not null
                && textureComponentIds.TryGetValue(material.TexturePath, out string? textureComponentId))
            {
                materialMembers["AlbedoTexture"] = new Reference
                {
                    TargetID = textureComponentId,
                };
            }

            await EnsureSlotAsync(
                client,
                materialSlotId,
                materialsSlotId,
                $"Material {materialIndex.ToString(CultureInfo.InvariantCulture)} {material.MaterialKey}",
                null,
                cancellationToken);
            await EnsureComponentAsync(
                client,
                materialSlotId,
                materialId,
                "[FrooxEngine]FrooxEngine.PBS_Metallic",
                materialMembers,
                cancellationToken);

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
}
