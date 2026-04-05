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

    private string? artifactPath;
    private Utf8JsonWriter? jsonWriter;
    private FileStream? outputStream;

    public async Task BeginAsync(
        ResoniteConstructionMetadata metadata,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        string rootDirectory = Path.GetFullPath(outputRoot);
        string artifactDirectory = Path.Combine(
            rootDirectory,
            SanitizePathSegment(metadata.Request.Dataset),
            SanitizePathSegment(metadata.Request.MeshCode));

        Directory.CreateDirectory(artifactDirectory);

        artifactPath = Path.Combine(artifactDirectory, "resonite-construction-plan.json");
        outputStream = new FileStream(
            artifactPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            useAsync: true);
        jsonWriter = new Utf8JsonWriter(
            outputStream,
            new JsonWriterOptions
            {
                Indented = true,
            });

        jsonWriter.WriteStartObject();
        jsonWriter.WriteString("schemaVersion", metadata.SchemaVersion);
        jsonWriter.WriteString("worldName", metadata.WorldName);
        jsonWriter.WritePropertyName("request");
        JsonSerializer.Serialize(jsonWriter, metadata.Request, JsonOptions);
        jsonWriter.WritePropertyName("sourceDataset");
        JsonSerializer.Serialize(jsonWriter, metadata.SourceDataset, JsonOptions);
        jsonWriter.WritePropertyName("localOrigin");
        JsonSerializer.Serialize(jsonWriter, metadata.LocalOrigin, JsonOptions);
        jsonWriter.WritePropertyName("cityObjects");
        jsonWriter.WriteStartArray();
        await jsonWriter.FlushAsync(cancellationToken);
    }

    public async Task ProcessCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        ObjectDisposedException.ThrowIf(jsonWriter is null, this);
        JsonSerializer.Serialize(jsonWriter, cityObject, JsonOptions);
        await jsonWriter.FlushAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(jsonWriter is null, this);
        ObjectDisposedException.ThrowIf(outputStream is null, this);

        jsonWriter.WriteEndArray();
        jsonWriter.WriteEndObject();
        await jsonWriter.FlushAsync(cancellationToken);
        await outputStream.FlushAsync(cancellationToken);

        return [artifactPath!];
    }

    public async ValueTask DisposeAsync()
    {
        if (jsonWriter is not null)
        {
            await jsonWriter.DisposeAsync();
            jsonWriter = null;
        }

        if (outputStream is not null)
        {
            await outputStream.DisposeAsync();
            outputStream = null;
        }

        artifactPath = null;
    }

    private static string SanitizePathSegment(string value)
    {
        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalidCharacters.Contains(character) ? '-' : character));
    }
}
