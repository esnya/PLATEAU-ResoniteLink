using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.IO.Compression;
using System.Linq;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: DefaultMaterialIngest <ambientcg|texturecan> <asset-id> <zip-path> <destination-directory>");
    return 2;
}

string sourceKind = args[0];
string assetId = args[1];
string zipPath = args[2];
string destinationDirectory = args[3];

if (!File.Exists(zipPath))
{
    Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Zip file was not found: {zipPath}"));
    return 2;
}

Directory.CreateDirectory(destinationDirectory);

using ZipArchive archive = ZipFile.OpenRead(zipPath);
IngestedMaps maps = sourceKind.ToLowerInvariant() switch
{
    "ambientcg" => IngestAmbientCg(archive, assetId, destinationDirectory),
    "texturecan" => IngestTextureCan(archive, assetId, destinationDirectory),
    _ => throw new InvalidOperationException($"Unknown source kind '{sourceKind}'."),
};

CreatePackedMetallicMap(maps, destinationDirectory);
return 0;

static IngestedMaps IngestAmbientCg(
    ZipArchive archive,
    string assetId,
    string destinationDirectory)
{
    string stem = $"{assetId}_2K-JPG";
    string? colorPath = CopyFirst(archive, destinationDirectory, $"{stem}_Color", $"{stem}_Color", [".jpg", ".png"]);
    string? heightPath = CopyFirst(archive, destinationDirectory, $"{stem}_Displacement", $"{stem}_Height", [".jpg", ".png"]);
    string? normalPath = CopyFirst(archive, destinationDirectory, $"{stem}_NormalGL", $"{stem}_NormalGL", [".jpg", ".png"]);
    string? emissionPath = CopyFirst(archive, destinationDirectory, $"{stem}_Emission", $"{stem}_Emission", [".jpg", ".png"]);
    string? roughnessPath = CopyFirst(archive, destinationDirectory, $"{stem}_Roughness", null, [".jpg", ".png"]);
    string? aoPath = CopyFirst(archive, destinationDirectory, $"{stem}_AmbientOcclusion", null, [".jpg", ".png"]);
    string? metalnessPath = CopyFirst(archive, destinationDirectory, $"{stem}_Metalness", null, [".jpg", ".png"]);

    if (colorPath is null)
    {
        throw new InvalidOperationException($"No color map was found for AmbientCG asset '{assetId}'.");
    }

    return new IngestedMaps(
        $"{stem}_Metallic.png",
        colorPath,
        heightPath,
        normalPath,
        emissionPath,
        roughnessPath,
        aoPath,
        metalnessPath);
}

static IngestedMaps IngestTextureCan(
    ZipArchive archive,
    string assetId,
    string destinationDirectory)
{
    string archiveStem = assetId.ToLowerInvariant();
    string outputStem = ToPascalStem(assetId);
    string? colorPath = CopyFirst(archive, destinationDirectory, $"{archiveStem}_color_2k", $"{outputStem}_2K_Color", [".jpg", ".png"]);
    string? heightPath = CopyFirst(archive, destinationDirectory, $"{archiveStem}_height_2k", $"{outputStem}_2K_Height", [".jpg", ".png"]);
    string? normalPath = CopyFirst(archive, destinationDirectory, $"{archiveStem}_normal_opengl_2k", $"{outputStem}_2K_NormalGL", [".jpg", ".png"]);
    string? emissionPath = CopyFirst(archive, destinationDirectory, $"{archiveStem}_emission_2k", $"{outputStem}_2K_Emission", [".jpg", ".png"]);
    string? roughnessPath = CopyFirst(archive, destinationDirectory, $"{archiveStem}_roughness_2k", null, [".jpg", ".png"]);
    string? aoPath = CopyFirst(archive, destinationDirectory, $"{archiveStem}_ao_2k", null, [".jpg", ".png"]);
    string? metalnessPath = CopyFirst(archive, destinationDirectory, $"{archiveStem}_metallic_2k", null, [".jpg", ".png"]);

    if (colorPath is null)
    {
        throw new InvalidOperationException($"No color map was found for TextureCan asset '{assetId}'.");
    }

    return new IngestedMaps(
        $"{outputStem}_2K_Metallic.png",
        colorPath,
        heightPath,
        normalPath,
        emissionPath,
        roughnessPath,
        aoPath,
        metalnessPath);
}

static string? CopyFirst(
    ZipArchive archive,
    string destinationDirectory,
    string sourceStem,
    string? destinationStem,
    IReadOnlyList<string> extensions)
{
    foreach (string extension in extensions)
    {
        ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(candidate =>
            string.Equals(Path.GetFileName(candidate.FullName), $"{sourceStem}{extension}", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            continue;
        }

        string destinationPath = destinationStem is null
            ? Path.Combine(Path.GetTempPath(), "PlateauResoniteLink", "material-ingest", $"{Guid.NewGuid():N}{extension}")
            : Path.Combine(destinationDirectory, $"{destinationStem}{extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        entry.ExtractToFile(destinationPath, overwrite: true);
        return destinationPath;
    }

    return null;
}

static void CreatePackedMetallicMap(
    IngestedMaps maps,
    string destinationDirectory)
{
    using Image<Rgba32> reference = Image.Load<Rgba32>(maps.ColorPath);
    using Image<Rgba32>? roughness = LoadIfPresent(maps.RoughnessPath);
    using Image<Rgba32>? ao = LoadIfPresent(maps.AmbientOcclusionPath);
    using Image<Rgba32>? metalness = LoadIfPresent(maps.MetalnessPath);
    using Image<Rgba32> packed = new(reference.Width, reference.Height);

    packed.ProcessPixelRows(accessor =>
    {
        for (int y = 0; y < accessor.Height; y++)
        {
            Span<Rgba32> row = accessor.GetRowSpan(y);
            for (int x = 0; x < row.Length; x++)
            {
                row[x] = new Rgba32(
                    SampleRedOrDefault(metalness, x, y, 0),
                    SampleRedOrDefault(ao, x, y, 255),
                    SampleRedOrDefault(roughness, x, y, 255),
                    255);
            }
        }
    });

    packed.SaveAsPng(Path.Combine(destinationDirectory, maps.PackedMetallicFileName));
}

static Image<Rgba32>? LoadIfPresent(string? path)
{
    return path is null ? null : Image.Load<Rgba32>(path);
}

static byte SampleRedOrDefault(Image<Rgba32>? image, int x, int y, byte fallback)
{
    if (image is null)
    {
        return fallback;
    }

    return image[x % image.Width, y % image.Height].R;
}

static string ToPascalStem(string assetId)
{
    return string.Concat(
        assetId.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(part.ToLowerInvariant())));
}

internal sealed record IngestedMaps(
    string PackedMetallicFileName,
    string ColorPath,
    string? HeightPath,
    string? NormalPath,
    string? EmissionPath,
    string? RoughnessPath,
    string? AmbientOcclusionPath,
    string? MetalnessPath);
