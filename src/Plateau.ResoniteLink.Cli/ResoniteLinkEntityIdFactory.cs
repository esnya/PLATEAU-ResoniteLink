namespace Plateau.ResoniteLink.Cli;

internal static class ResoniteLinkEntityIdFactory
{
    public static string CreateDatasetScopedEntityId(
        string dataset,
        string kind,
        string? suffix = null)
    {
        return CreateDatasetScopedEntityId(dataset, kind, suffix is null ? [] : [suffix]);
    }

    public static string CreateDatasetScopedEntityId(
        string dataset,
        string kind,
        params string?[] suffixes)
    {
        return AppendSuffixes(
            BuildBaseId(SanitizeId(dataset), kind),
            suffixes);
    }

    public static string CreateStableEntityId(
        string dataset,
        string meshCode,
        string kind,
        string? suffix = null)
    {
        return AppendSuffixes(
            BuildBaseId(
                SanitizeId(dataset),
                SanitizeId(meshCode),
                kind),
            suffix is null ? [] : [suffix]);
    }

    public static string CreateStableEntityId(
        string dataset,
        string meshCode,
        string kind,
        params string?[] suffixes)
    {
        return AppendSuffixes(
            BuildBaseId(
                SanitizeId(dataset),
                SanitizeId(meshCode),
                kind),
            suffixes);
    }

    public static string CreateEntityId(
        string dataset,
        string meshCode,
        string kind,
        string buildNonce,
        string? suffix = null)
    {
        return CreateEntityId(dataset, meshCode, kind, buildNonce, suffix is null ? [] : [suffix]);
    }

    public static string CreateEntityId(
        string dataset,
        string meshCode,
        string kind,
        string buildNonce,
        params string?[] suffixes)
    {
        string baseId = AppendSuffixes(
            BuildBaseId(
                SanitizeId(dataset),
                SanitizeId(meshCode),
                kind),
            suffixes);

        return $"{baseId}_{buildNonce}";
    }

    private static string SanitizeId(string value)
    {
        return string.Concat(value.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
    }

    private static string BuildBaseId(params string[] parts)
    {
        return $"PlateauResoniteLink_{string.Join("_", parts.Select(SanitizeId))}";
    }

    private static string AppendSuffixes(string baseId, IEnumerable<string?> suffixes)
    {
        foreach (string? suffix in suffixes)
        {
            if (!string.IsNullOrWhiteSpace(suffix))
            {
                baseId = $"{baseId}_{SanitizeId(suffix)}";
            }
        }

        return baseId;
    }
}
