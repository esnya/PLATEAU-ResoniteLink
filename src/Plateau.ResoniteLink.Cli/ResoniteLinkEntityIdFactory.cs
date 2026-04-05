namespace Plateau.ResoniteLink.Cli;

internal static class ResoniteLinkEntityIdFactory
{
    public static string CreateDatasetScopedEntityId(
        string dataset,
        string kind,
        string? suffix = null)
    {
        string baseId = BuildBaseId(SanitizeId(dataset), kind);
        if (!string.IsNullOrWhiteSpace(suffix))
        {
            baseId = $"{baseId}_{SanitizeId(suffix)}";
        }

        return baseId;
    }

    public static string CreateStableEntityId(
        string dataset,
        string meshCode,
        string kind,
        string? suffix = null)
    {
        string baseId = BuildBaseId(
            SanitizeId(dataset),
            SanitizeId(meshCode),
            kind);
        if (!string.IsNullOrWhiteSpace(suffix))
        {
            baseId = $"{baseId}_{SanitizeId(suffix)}";
        }

        return baseId;
    }

    public static string CreateEntityId(
        string dataset,
        string meshCode,
        string kind,
        string buildNonce,
        string? suffix = null)
    {
        string baseId = BuildBaseId(
            SanitizeId(dataset),
            SanitizeId(meshCode),
            kind);
        if (!string.IsNullOrWhiteSpace(suffix))
        {
            baseId = $"{baseId}_{SanitizeId(suffix)}";
        }

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
}
