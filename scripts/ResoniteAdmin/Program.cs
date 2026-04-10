using ResoniteLink;

if (args.Length < 2)
{
    await Console.Error.WriteLineAsync("Usage: ResoniteAdmin <endpoint> <dataset>");
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
