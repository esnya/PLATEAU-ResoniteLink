using System.Text.Json;
using System.Text.Json.Serialization;

using ResoniteAdmin;

using ResoniteLink;

JsonSerializerOptions dumpJsonOptions = new()
{
    WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
};

if (!ResoniteAdminCommandLineParser.TryParse(args, out ResoniteAdminCommandLineOptions? options, out string? error))
{
    await Console.Error.WriteLineAsync(error);
    await Console.Error.WriteLineAsync(ResoniteAdminCommandLineParser.UsageText);
    return 1;
}

using LinkInterface link = new();
using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

await link.Connect(options!.Endpoint, cts.Token);

return options.Kind switch
{
    ResoniteAdminCommandKind.CleanupDataset => await ExecuteCleanupDatasetAsync(link, options),
    ResoniteAdminCommandKind.DumpRoot => await ExecuteDumpRootAsync(link, options),
    _ => throw new InvalidOperationException($"Unsupported command kind '{options.Kind}'."),
};

async Task<int> ExecuteCleanupDatasetAsync(LinkInterface link, ResoniteAdminCommandLineOptions options)
{
    SlotData root = await GetRootAsync(link, depth: 1, includeComponentData: false);
    if (!root.Success)
    {
        await Console.Error.WriteLineAsync(string.IsNullOrWhiteSpace(root.ErrorInfo)
            ? "GetSlot Root failed."
            : $"GetSlot Root failed: {root.ErrorInfo}");
        return 2;
    }

    string datasetRootName = $"PLATEAU {options.Dataset}";
    Slot[] targets = (root.Data?.Children ?? [])
        .Where(child => string.Equals(child.Name?.Value, datasetRootName, StringComparison.Ordinal))
        .ToArray();

    await Console.Out.WriteLineAsync($"Found {targets.Length} dataset root slot(s) named '{datasetRootName}'.");
    if (targets.Length == 0 && options.ListOnly)
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

        if (options.ListOnly)
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

async Task<int> ExecuteDumpRootAsync(LinkInterface link, ResoniteAdminCommandLineOptions options)
{
    SlotData root = await GetRootAsync(link, options.Depth, options.IncludeComponentData);
    if (!root.Success)
    {
        await Console.Error.WriteLineAsync(string.IsNullOrWhiteSpace(root.ErrorInfo)
            ? "GetSlot Root failed."
            : $"GetSlot Root failed: {root.ErrorInfo}");
        return 2;
    }

    var dump = new
    {
        Endpoint = options.Endpoint.ToString(),
        CapturedAtUtc = DateTimeOffset.UtcNow,
        Depth = options.Depth,
        IncludeComponentData = options.IncludeComponentData,
        Root = root.Data,
    };

    string json = JsonSerializer.Serialize(dump, dumpJsonOptions);

    if (!string.IsNullOrWhiteSpace(options.OutputPath))
    {
        string fullOutputPath = Path.GetFullPath(options.OutputPath);
        string? directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(fullOutputPath, json);
        await Console.Out.WriteLineAsync($"Root dump written to '{fullOutputPath}'.");
        return 0;
    }

    await Console.Out.WriteLineAsync(json);
    return 0;
}

Task<SlotData> GetRootAsync(LinkInterface link, int depth, bool includeComponentData)
{
    return link.GetSlotData(
        new GetSlot
        {
            SlotID = "Root",
            Depth = depth,
            IncludeComponentData = includeComponentData,
        });
}
