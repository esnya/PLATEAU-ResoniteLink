using System.Text.Json;

namespace ResoniteSessionTool;

internal static class RootDumpCleanupTargets
{
    public static List<SlotSummary> FindDatasetRootTargets(RootDump dump, string datasetRootName)
    {
        return EnumerateRootChildren(dump)
            .Where(child => string.Equals(child.Name, datasetRootName, StringComparison.Ordinal))
            .ToList();
    }

    public static IReadOnlyList<SlotSummary> EnumerateRootChildren(RootDump dump)
    {
        return EnumerateDirectChildren(dump.Root);
    }

    public static IReadOnlyList<SlotSummary> EnumerateDirectChildren(object? slotData)
    {
        if (slotData is null)
        {
            return Array.Empty<SlotSummary>();
        }

        JsonElement rootElement = JsonSerializer.SerializeToElement(slotData);
        if (!TryGetPropertyIgnoreCase(rootElement, "children", out JsonElement childrenElement) || (childrenElement.ValueKind != JsonValueKind.Array))
        {
            return Array.Empty<SlotSummary>();
        }

        List<SlotSummary> children = [];
        foreach (JsonElement child in childrenElement.EnumerateArray())
        {
            string? id = TryReadNestedStringIgnoreCase(child, "id");
            string? name = TryReadNestedStringIgnoreCase(child, "name", "value");

            if (!string.IsNullOrWhiteSpace(id))
            {
                children.Add(new SlotSummary(id, name ?? string.Empty));
            }
        }

        return children;
    }

    private static string? TryReadNestedStringIgnoreCase(JsonElement element, params string[] propertyPath)
    {
        JsonElement current = element;
        foreach (string property in propertyPath)
        {
            if ((current.ValueKind != JsonValueKind.Object) || !TryGetPropertyIgnoreCase(current, property, out JsonElement nested))
            {
                return null;
            }

            current = nested;
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString()
            : current.ToString();
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
