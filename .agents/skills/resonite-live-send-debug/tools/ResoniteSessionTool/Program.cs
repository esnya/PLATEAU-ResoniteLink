using System.Text.Json;
using System.Text.Json.Serialization;

using ResoniteLink;

using ResoniteSessionTool;

JsonSerializerOptions dumpJsonOptions = new()
{
    WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
};

if (!ResoniteSessionToolCommandLineParser.TryParse(args, out ResoniteSessionToolCommandLineOptions? options, out string? error))
{
    await Console.Error.WriteLineAsync(error);
    await Console.Error.WriteLineAsync(ResoniteSessionToolCommandLineParser.UsageText);
    return 1;
}

using LinkInterface link = new();
using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

await link.Connect(options!.Endpoint, cts.Token);

return options.Kind switch
{
    ResoniteSessionToolCommandKind.DumpRoot => await ExecuteDumpRootAsync(link, options),
    ResoniteSessionToolCommandKind.RemoveSlot => await ExecuteRemoveSlotAsync(link, options),
    _ => throw new InvalidOperationException($"Unsupported command kind '{options.Kind}'."),
};

async Task<int> ExecuteDumpRootAsync(LinkInterface link, ResoniteSessionToolCommandLineOptions options)
{
    SlotData root = await link.GetSlotData(
        new GetSlot
        {
            SlotID = "Root",
            Depth = options.Depth,
            IncludeComponentData = options.IncludeComponentData,
        });

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

async Task<int> ExecuteRemoveSlotAsync(LinkInterface link, ResoniteSessionToolCommandLineOptions options)
{
    Response response = await link.RemoveSlot(
        new RemoveSlot
        {
            SlotID = options.SlotId!,
        });

    if (!response.Success)
    {
        await Console.Error.WriteLineAsync(string.IsNullOrWhiteSpace(response.ErrorInfo)
            ? $"RemoveSlot failed for '{options.SlotId}'."
            : $"RemoveSlot failed for '{options.SlotId}': {response.ErrorInfo}");
        return 3;
    }

    await Console.Out.WriteLineAsync($"Removed slot '{options.SlotId}'.");
    return 0;
}
