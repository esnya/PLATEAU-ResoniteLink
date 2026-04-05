using System.Text.Json;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

public sealed class JsonArtifactResoniteSceneBuilder : IResoniteSceneBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public async Task<IReadOnlyList<string>> BuildAsync(
        ResoniteConstructionPlan plan,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        string rootDirectory = Path.GetFullPath(outputRoot);
        string artifactDirectory = Path.Combine(
            rootDirectory,
            SanitizePathSegment(plan.Request.Dataset),
            SanitizePathSegment(plan.Request.MeshCode));

        Directory.CreateDirectory(artifactDirectory);

        string artifactPath = Path.Combine(artifactDirectory, "resonite-construction-plan.json");
        string json = JsonSerializer.Serialize(plan, JsonOptions);
        await File.WriteAllTextAsync(artifactPath, json, cancellationToken);

        return [artifactPath];
    }

    private static string SanitizePathSegment(string value)
    {
        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalidCharacters.Contains(character) ? '-' : character));
    }
}
