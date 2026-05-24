using System.Text.RegularExpressions;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class ConcreteMeshCodeResolver
{
    private static readonly Regex MeshCodeTokenRegex = new(
        @"(?<!\d)(\d{8})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string ResolveActualMeshCode(
        string displayName,
        string objectId,
        string fallbackActualMeshCode)
    {
        return TryResolve(displayName, out string? displayNameMeshCode)
            ? displayNameMeshCode!
            : TryResolve(objectId, out string? objectIdMeshCode)
                ? objectIdMeshCode!
                : fallbackActualMeshCode;
    }

    private static bool TryResolve(string value, out string? meshCode)
    {
        meshCode = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        Match match = MeshCodeTokenRegex.Match(value);
        if (!match.Success)
        {
            return false;
        }

        string candidate = match.Groups[1].Value;
        if (!PlateauMeshCode.TryGetBounds(candidate, out _))
        {
            return false;
        }

        meshCode = candidate;
        return true;
    }
}
