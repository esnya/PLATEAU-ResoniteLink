using System.Globalization;
using System.Text.RegularExpressions;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Targets.Resonite;

internal static partial class ResoniteSourceMeshCodeAnchor
{
    [GeneratedRegex(@"(?<!\d)(\d{8}|\d{6})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex MeshCodeRegex();

    public static bool TryGetConcreteMeshCode(string value, out string meshCode)
    {
        meshCode = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        Match[] matches = MeshCodeRegex().Matches(value).Cast<Match>().ToArray();
        foreach (Match match in matches.OrderByDescending(static entry => entry.Value.Length).ThenByDescending(static entry => entry.Index))
        {
            if (PlateauMeshCode.TryGetCenter(match.Value, out _))
            {
                meshCode = match.Value;
                return true;
            }
        }

        return false;
    }

    public static string ResolveCompletionMeshCode(SceneBootstrapInfo setupInfo)
    {
        ArgumentNullException.ThrowIfNull(setupInfo);

        string[] concreteSourceMeshCodes = EnumerateSourceFileMeshCodes(
            setupInfo.SourceFiles,
            includeDemPackages: false)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (concreteSourceMeshCodes.Length == 0)
        {
            concreteSourceMeshCodes = EnumerateSourceFileMeshCodes(
                setupInfo.SourceFiles,
                includeDemPackages: true)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        if (concreteSourceMeshCodes.Length == 0)
        {
            concreteSourceMeshCodes = setupInfo.RequestedMeshCodes
                .Where(static candidate => PlateauMeshCode.TryGetCenter(candidate, out _))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        if (concreteSourceMeshCodes.Length == 0)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Live Offset V2 requires at least one concrete meshcode from discovered source filenames, and request mesh '{setupInfo.MeshCode}' was not concrete."));
        }

        return ResolveMeshCodeClosestToBoundsCenter(concreteSourceMeshCodes);
    }

    public static string ResolveCompletionMeshCode(ResoniteConstructionMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return ResolveCompletionMeshCode(
            SceneBootstrapInfo.CreateFromMetadata(metadata));
    }

    private static IEnumerable<string> EnumerateSourceFileMeshCodes(
        IReadOnlyList<string> sourceFiles,
        bool includeDemPackages)
    {
        foreach (string sourceFile in sourceFiles.Where(static path => !string.IsNullOrWhiteSpace(path)))
        {
            string normalizedPath = sourceFile.Replace('\\', '/');
            if (!includeDemPackages && IsDemPackagePath(normalizedPath))
            {
                continue;
            }

            string fileName = Path.GetFileNameWithoutExtension(normalizedPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            if (TryGetConcreteMeshCode(fileName, out string meshCode))
            {
                yield return meshCode;
            }
        }
    }

    private static string ResolveMeshCodeClosestToBoundsCenter(IReadOnlyList<string> meshCodes)
    {
        List<(double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude)> candidateBounds = [];
        foreach (string meshCode in meshCodes)
        {
            if (PlateauMeshCode.TryGetBounds(meshCode, out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) resolvedBounds))
            {
                candidateBounds.Add(resolvedBounds);
            }
        }

        (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds = candidateBounds
            .Aggregate(
                static (left, right) => (
                    SouthLatitude: Math.Min(left.SouthLatitude, right.SouthLatitude),
                    NorthLatitude: Math.Max(left.NorthLatitude, right.NorthLatitude),
                    WestLongitude: Math.Min(left.WestLongitude, right.WestLongitude),
                    EastLongitude: Math.Max(left.EastLongitude, right.EastLongitude)));
        ResoniteLocalOrigin boundsCenter = new(
            Latitude: (bounds.SouthLatitude + bounds.NorthLatitude) / 2.0,
            Longitude: (bounds.WestLongitude + bounds.EastLongitude) / 2.0,
            Altitude: 0.0);

        return meshCodes
            .Select(meshCode => (MeshCode: meshCode, DistanceSquared: ComputeDistanceSquared(boundsCenter, meshCode)))
            .OrderBy(static candidate => candidate.DistanceSquared)
            .ThenByDescending(static candidate => candidate.MeshCode.Length)
            .ThenBy(static candidate => candidate.MeshCode, StringComparer.Ordinal)
            .First()
            .MeshCode;
    }

    private static double ComputeDistanceSquared(
        ResoniteLocalOrigin center,
        string meshCode)
    {
        if (!PlateauMeshCode.TryGetCenter(meshCode, out ResoniteLocalOrigin meshCenter))
        {
            return double.PositiveInfinity;
        }

        double latitudeDelta = meshCenter.Latitude - center.Latitude;
        double longitudeDelta = meshCenter.Longitude - center.Longitude;
        return (latitudeDelta * latitudeDelta) + (longitudeDelta * longitudeDelta);
    }

    private static bool IsDemPackagePath(string normalizedPath)
    {
        string[] segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2
            && string.Equals(segments[0], "udx", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[1], "dem", StringComparison.OrdinalIgnoreCase);
    }
}
