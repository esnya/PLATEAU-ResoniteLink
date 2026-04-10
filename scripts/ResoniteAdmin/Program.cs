using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using ResoniteLink;

return await ProgramMainAsync(args);

static async Task<int> ProgramMainAsync(string[] args)
{
    if (args.Length == 0)
    {
        await Console.Error.WriteLineAsync(
            """
            Usage:
              ResoniteAdmin <endpoint> <dataset> [--list-only]
              ResoniteAdmin probe-material-property-blocks <endpoint> [--keep-root]
            """);
        return 1;
    }

    if (string.Equals(args[0], "probe-material-property-blocks", StringComparison.Ordinal))
    {
        return await RunMaterialPropertyBlockProbeAsync(args);
    }

    return await RunDatasetRootCommandAsync(args);
}

static async Task<int> RunDatasetRootCommandAsync(string[] args)
{
    if (args.Length < 2)
    {
        await Console.Error.WriteLineAsync("Usage: ResoniteAdmin <endpoint> <dataset> [--list-only]");
        return 1;
    }

    Uri endpoint = new(args[0], UriKind.Absolute);
    string dataset = args[1];
    string datasetRootName = $"PLATEAU {dataset}";
    bool listOnly = args.Any(static argument => string.Equals(argument, "--list-only", StringComparison.Ordinal));

    using LinkInterface link = new();
    using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

    await link.Connect(endpoint, cts.Token);

    SlotData root = await link.GetSlotData(
        new GetSlot
        {
            SlotID = "Root",
            Depth = 1,
            IncludeComponentData = false,
        });

    if (!root.Success)
    {
        await Console.Error.WriteLineAsync(string.IsNullOrWhiteSpace(root.ErrorInfo)
            ? "GetSlot Root failed."
            : $"GetSlot Root failed: {root.ErrorInfo}");
        return 2;
    }

    Slot[] targets = (root.Data?.Children ?? [])
        .Where(child => string.Equals(child.Name?.Value, datasetRootName, StringComparison.Ordinal))
        .ToArray();

    await Console.Out.WriteLineAsync($"Found {targets.Length} dataset root slot(s) named '{datasetRootName}'.");
    if (targets.Length == 0 && listOnly)
    {
        foreach (Slot child in root.Data?.Children ?? [])
        {
            await Console.Out.WriteLineAsync($"Root child: {child.ID} :: {child.Name?.Value}");
        }
    }

    foreach (Slot target in targets)
    {
        if (string.IsNullOrWhiteSpace(target.ID))
        {
            await Console.Out.WriteLineAsync("Skipping unnamed-id slot match.");
            continue;
        }

        if (listOnly)
        {
            continue;
        }

        await Console.Out.WriteLineAsync("Warning: removing this slot destroys the matching dataset root in the current live Resonite session.");
        await Console.Out.WriteLineAsync($"Removing slot '{target.ID}' ({target.Name?.Value}).");
        Response response = await link.RemoveSlot(
            new RemoveSlot
            {
                SlotID = target.ID,
            });

        if (!response.Success)
        {
            await Console.Error.WriteLineAsync(string.IsNullOrWhiteSpace(response.ErrorInfo)
                ? $"RemoveSlot failed for '{target.ID}'."
                : $"RemoveSlot failed for '{target.ID}': {response.ErrorInfo}");
            return 3;
        }
    }

    return 0;
}

static async Task<int> RunMaterialPropertyBlockProbeAsync(string[] args)
{
    if (args.Length < 2)
    {
        await Console.Error.WriteLineAsync("Usage: ResoniteAdmin probe-material-property-blocks <endpoint> [--keep-root]");
        return 1;
    }

    Uri endpoint = new(args[1], UriKind.Absolute);
    bool keepRoot = args.Any(static argument => string.Equals(argument, "--keep-root", StringComparison.Ordinal));
    string probeName = string.Create(
        CultureInfo.InvariantCulture,
        $"Codex.MaterialPropertyBlocks.Probe.{DateTimeOffset.UtcNow:yyyyMMddTHHmmss}");

    using LinkInterface link = new();
    using CancellationTokenSource cts = new(TimeSpan.FromSeconds(60));

    await link.Connect(endpoint, cts.Token);

    string probeRootId = await AddSlotAsync(link, "Root", probeName, cts.Token);
    await Console.Out.WriteLineAsync($"ProbeRoot={probeRootId}");
    await Console.Out.WriteLineAsync("Probe scope is isolated from existing dataset roots.");

    try
    {
        string assetsRootId = await AddSlotAsync(link, probeRootId, "Assets", cts.Token);
        string meshAssetSlotId = await AddSlotAsync(link, assetsRootId, "Mesh", cts.Token);
        string materialAssetSlotId = await AddSlotAsync(link, assetsRootId, "Material", cts.Token);
        string propertyBlockAssetSlotId = await AddSlotAsync(link, assetsRootId, "PropertyBlock", cts.Token);

        Uri meshUri = EnsureAssetUrl(await link.ImportMesh(CreateProbeMeshImport()), "import mesh");
        Uri textureUri = EnsureAssetUrl(await link.ImportTexture(CreateProbeTextureImport(0, 0, 255, 255)), "import texture");

        string meshComponentId = await AddAssetComponentAsync(
            link,
            meshAssetSlotId,
            "[FrooxEngine]FrooxEngine.StaticMesh",
            meshUri);
        string materialComponentId = await AddComponentAsync(
            link,
            materialAssetSlotId,
            "[FrooxEngine]FrooxEngine.PBS_Metallic",
            new Dictionary<string, Member>(StringComparer.Ordinal));
        string textureComponentId = await AddAssetComponentAsync(
            link,
            propertyBlockAssetSlotId,
            "[FrooxEngine]FrooxEngine.StaticTexture2D",
            textureUri);
        string propertyBlockComponentId = await AddComponentAsync(
            link,
            propertyBlockAssetSlotId,
            "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Texture"] = new Reference
                {
                    TargetID = textureComponentId,
                },
            });

        ProbeCase[] probeCases =
        [
            new(
                "dense-repeated",
                [
                    CreateReference(propertyBlockComponentId),
                    CreateReference(propertyBlockComponentId),
                ]),
            new(
                "sparse-trailing-omitted",
                [
                    CreateReference(propertyBlockComponentId),
                ]),
            new(
                "sparse-trailing-empty",
                [
                    CreateReference(propertyBlockComponentId),
                    new EmptyElement(),
                ]),
            new(
                "sparse-leading-empty",
                [
                    new EmptyElement(),
                    CreateReference(propertyBlockComponentId),
                ]),
            new(
                "sparse-trailing-null-member",
                [
                    CreateReference(propertyBlockComponentId),
                    null!,
                ]),
            new(
                "sparse-leading-null-member",
                [
                    null!,
                    CreateReference(propertyBlockComponentId),
                ]),
            new(
                "sparse-trailing-null-target",
                [
                    CreateReference(propertyBlockComponentId),
                    new Reference(),
                ]),
            new(
                "sparse-leading-null-target",
                [
                    new Reference(),
                    CreateReference(propertyBlockComponentId),
                ]),
        ];

        List<ProbeResult> results = [];
        foreach (ProbeCase probeCase in probeCases)
        {
            string objectSlotId = await AddSlotAsync(link, probeRootId, $"Probe-{probeCase.Name}", cts.Token);
            ProbeResult result = await RunSingleProbeCaseAsync(
                link,
                objectSlotId,
                meshComponentId,
                materialComponentId,
                probeCase,
                cts.Token);
            results.Add(result);
            await Console.Out.WriteLineAsync(
                $"{probeCase.Name}: {(result.Success ? "success" : "failure")} :: {result.Message}");
        }

        await Console.Out.WriteLineAsync("ProbeSummaryStart");
        foreach (ProbeResult result in results)
        {
            await Console.Out.WriteLineAsync(
                $"{result.Name}|success={result.Success}|slot={result.ObjectSlotId}|message={result.Message}");
        }
        await Console.Out.WriteLineAsync("ProbeSummaryEnd");
    }
    finally
    {
        if (!keepRoot)
        {
            Response cleanup = await link.RemoveSlot(
                new RemoveSlot
                {
                    SlotID = probeRootId,
                });
            await Console.Out.WriteLineAsync(
                cleanup.Success
                    ? $"ProbeCleanup=removed:{probeRootId}"
                    : $"ProbeCleanup=failed:{probeRootId}:{cleanup.ErrorInfo}");
        }
        else
        {
            await Console.Out.WriteLineAsync($"ProbeCleanup=kept:{probeRootId}");
        }
    }

    return 0;
}

[SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The probe records direct ResoniteLink mutation acceptance per case and must continue to the remaining cases after a failure.")]
static async Task<ProbeResult> RunSingleProbeCaseAsync(
    LinkInterface link,
    string objectSlotId,
    string meshComponentId,
    string materialComponentId,
    ProbeCase probeCase,
    CancellationToken cancellationToken)
{
    try
    {
        string rendererId = await AddComponentAsync(
            link,
            objectSlotId,
            "[FrooxEngine]FrooxEngine.MeshRenderer",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Mesh"] = CreateReference(meshComponentId),
                ["Materials"] = new SyncList
                {
                    Elements =
                    [
                        CreateReference(materialComponentId),
                        CreateReference(materialComponentId),
                    ],
                },
                ["MaterialPropertyBlocks"] = new SyncList
                {
                    Elements = probeCase.PropertyBlockElements.ToList(),
                },
            });

        cancellationToken.ThrowIfCancellationRequested();
        return new ProbeResult(probeCase.Name, objectSlotId, true, $"renderer={rendererId}");
    }
    catch (Exception exception)
    {
        return new ProbeResult(probeCase.Name, objectSlotId, false, exception.Message);
    }
}

static ImportMeshRawData CreateProbeMeshImport()
{
    ImportMeshRawData request = new()
    {
        VertexCount = 4,
        HasNormals = true,
        HasTangents = false,
        HasColors = false,
        BoneWeightCount = 0,
        UV_Channel_Dimensions = [2],
        Submeshes =
        [
            new TriangleSubmeshRawData
            {
                TriangleCount = 1,
            },
            new TriangleSubmeshRawData
            {
                TriangleCount = 1,
            },
        ],
        Bones = [],
        BlendShapes = [],
    };

    request.AllocateBuffer();

    request.Positions[0] = new float3 { x = 0.0f, y = 0.0f, z = 0.0f };
    request.Positions[1] = new float3 { x = 1.0f, y = 0.0f, z = 0.0f };
    request.Positions[2] = new float3 { x = 0.0f, y = 1.0f, z = 0.0f };
    request.Positions[3] = new float3 { x = 1.0f, y = 1.0f, z = 0.0f };

    for (int index = 0; index < request.VertexCount; index++)
    {
        request.Normals[index] = new float3 { x = 0.0f, y = 0.0f, z = -1.0f };
    }

    request.AccessUV_2D(0)[0] = new float2 { x = 0.0f, y = 0.0f };
    request.AccessUV_2D(0)[1] = new float2 { x = 1.0f, y = 0.0f };
    request.AccessUV_2D(0)[2] = new float2 { x = 0.0f, y = 1.0f };
    request.AccessUV_2D(0)[3] = new float2 { x = 1.0f, y = 1.0f };

    ((TriangleSubmeshRawData)request.Submeshes[0]).Indices[0] = 0;
    ((TriangleSubmeshRawData)request.Submeshes[0]).Indices[1] = 1;
    ((TriangleSubmeshRawData)request.Submeshes[0]).Indices[2] = 2;
    ((TriangleSubmeshRawData)request.Submeshes[1]).Indices[0] = 1;
    ((TriangleSubmeshRawData)request.Submeshes[1]).Indices[1] = 3;
    ((TriangleSubmeshRawData)request.Submeshes[1]).Indices[2] = 2;

    return request;
}

static ImportTexture2DRawData CreateProbeTextureImport(byte r, byte g, byte b, byte a)
{
    return new ImportTexture2DRawData
    {
        Width = 1,
        Height = 1,
        ColorProfile = "sRGB",
        RawBinaryPayload = [r, g, b, a],
    };
}

static async Task<string> AddSlotAsync(
    LinkInterface link,
    string parentId,
    string name,
    CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    NewEntityId response = await link.AddSlot(
        new AddSlot
        {
            Data = new Slot
            {
                Parent = new Reference
                {
                    TargetID = parentId,
                },
                Name = new Field_string
                {
                    Value = name,
                },
            },
        });
    return EnsureCreatedId(response, $"add slot '{name}'");
}

static async Task<string> AddAssetComponentAsync(
    LinkInterface link,
    string containerSlotId,
    string componentType,
    Uri assetUri)
{
    return await AddComponentAsync(
        link,
        containerSlotId,
        componentType,
        new Dictionary<string, Member>(StringComparer.Ordinal)
        {
            ["URL"] = new Field_Uri
            {
                Value = assetUri,
            },
        });
}

static async Task<string> AddComponentAsync(
    LinkInterface link,
    string containerSlotId,
    string componentType,
    IReadOnlyDictionary<string, Member> members)
{
    NewEntityId response = await link.AddComponent(
        new AddComponent
        {
            ContainerSlotId = containerSlotId,
            Data = new Component
            {
                ComponentType = componentType,
                Members = new Dictionary<string, Member>(members, StringComparer.Ordinal),
            },
        });
    return EnsureCreatedId(response, $"add component '{componentType}'");
}

static string EnsureCreatedId(NewEntityId? response, string operation)
{
    if (response is null)
    {
        throw new InvalidOperationException($"ResoniteLink {operation} returned a null response.");
    }

    if (!response.Success)
    {
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(response.ErrorInfo)
                ? $"ResoniteLink {operation} failed."
                : $"ResoniteLink {operation} failed: {response.ErrorInfo}");
    }

    if (string.IsNullOrWhiteSpace(response.EntityId))
    {
        throw new InvalidOperationException($"ResoniteLink {operation} returned a null entity id.");
    }

    return response.EntityId;
}

static Uri EnsureAssetUrl(AssetData? response, string operation)
{
    if (response is null)
    {
        throw new InvalidOperationException($"ResoniteLink {operation} returned a null response.");
    }

    if (!response.Success)
    {
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(response.ErrorInfo)
                ? $"ResoniteLink {operation} failed."
                : $"ResoniteLink {operation} failed: {response.ErrorInfo}");
    }

    return response.AssetURL
        ?? throw new InvalidOperationException($"ResoniteLink {operation} returned a null asset URL.");
}

static Reference CreateReference(string targetId)
{
    return new Reference
    {
        TargetID = targetId,
    };
}

internal sealed record ProbeCase(string Name, IReadOnlyList<Member> PropertyBlockElements);

internal sealed record ProbeResult(string Name, string ObjectSlotId, bool Success, string Message);
