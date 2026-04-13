using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Plateau.ResoniteLink.Cli;

internal sealed class Lod2AtlasCityObjectBaker(
    ResoniteTextureImageLoader textureImageLoader,
    ResoniteTextureImportRegistry textureImportRegistry,
    int maxAtlasSize = 2048,
    int tilePaddingPixels = 2) : IResoniteBufferedCityObjectBaker
{
    internal const int DefaultMaxAtlasSize = 2048;
    internal const int DefaultTilePaddingPixels = 2;

    private readonly Dictionary<SourceUnitKey, List<ResoniteConstructionCityObject>> bufferedCityObjectsBySourceUnit = [];

    public string Name => "LOD2AtlasBake";

    public int BakedInputCityObjectCount { get; private set; }

    public int BakedOutputCityObjectCount { get; private set; }

    public ValueTask<bool> TryBufferAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        if (!CanBake(cityObject))
        {
            return ValueTask.FromResult(false);
        }

        SourceUnitKey sourceUnitKey = CreateSourceUnitKey(cityObject);
        if (!bufferedCityObjectsBySourceUnit.TryGetValue(sourceUnitKey, out List<ResoniteConstructionCityObject>? bufferedCityObjects))
        {
            bufferedCityObjects = [];
            bufferedCityObjectsBySourceUnit.Add(sourceUnitKey, bufferedCityObjects);
        }

        bufferedCityObjects.Add(cityObject);
        BakedInputCityObjectCount++;
        return ValueTask.FromResult(true);
    }

    public async Task<IReadOnlyList<ResoniteConstructionCityObject>> FlushAllAsync(
        CancellationToken cancellationToken = default)
    {
        if (bufferedCityObjectsBySourceUnit.Count == 0)
        {
            return [];
        }

        List<ResoniteConstructionCityObject> bakedCityObjects = [];
        foreach ((SourceUnitKey sourceUnitKey, List<ResoniteConstructionCityObject> cityObjects) in bufferedCityObjectsBySourceUnit
                     .OrderBy(static pair => pair.Key, SourceUnitKeyComparer.Instance))
        {
            IReadOnlyList<ResoniteConstructionCityObject> bakedSourceUnitCityObjects = await BakeSourceUnitAsync(
                sourceUnitKey,
                cityObjects,
                cancellationToken);
            bakedCityObjects.AddRange(bakedSourceUnitCityObjects);
        }

        bufferedCityObjectsBySourceUnit.Clear();
        return bakedCityObjects;
    }

    private static bool CanBake(ResoniteConstructionCityObject cityObject)
    {
        return PlateauPackageCatalog.IsBuildingPackage(cityObject.PackageName)
            && cityObject.LodLevel == 2
            && cityObject.Geometry is ResoniteTriangleMeshGeometry
            && cityObject.Transform.Rotation is null;
    }

    private async Task<IReadOnlyList<ResoniteConstructionCityObject>> BakeSourceUnitAsync(
        SourceUnitKey sourceUnitKey,
        IReadOnlyList<ResoniteConstructionCityObject> cityObjects,
        CancellationToken cancellationToken)
    {
        List<AtlasBatchEntry> entries = await CreateEntriesAsync(cityObjects, cancellationToken);
        if (entries.Count == 0)
        {
            return [];
        }

        try
        {
            List<IReadOnlyList<AtlasBatchEntry>> entryBatches = BuildAtlasEntryBatches(entries);
            List<ResoniteConstructionCityObject> bakedCityObjects = new(entryBatches.Count);
            for (int batchIndex = 0; batchIndex < entryBatches.Count; batchIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ResoniteConstructionCityObject bakedCityObject = await BakeBatchAsync(
                    sourceUnitKey,
                    cityObjects,
                    entryBatches[batchIndex],
                    batchIndex,
                    entryBatches.Count,
                    cancellationToken);
                bakedCityObjects.Add(bakedCityObject);
            }

            BakedOutputCityObjectCount += bakedCityObjects.Count;
            return bakedCityObjects;
        }
        finally
        {
            foreach (Image<Rgba32> tileImage in entries
                         .Select(static entry => entry.Tile.Image)
                         .Distinct())
            {
                tileImage.Dispose();
            }
        }
    }

    private async Task<List<AtlasBatchEntry>> CreateEntriesAsync(
        IReadOnlyList<ResoniteConstructionCityObject> cityObjects,
        CancellationToken cancellationToken)
    {
        List<AtlasBatchEntry> entries = [];

        foreach (ResoniteConstructionCityObject cityObject in cityObjects.OrderBy(static candidate => candidate.SlotKey, StringComparer.Ordinal))
        {
            Dictionary<int, ResoniteMaterialBinding> materialBySubmeshIndex = cityObject.Materials
                .SelectMany(material => material.SubmeshIndices.Select(submeshIndex => (submeshIndex, material)))
                .ToDictionary(static pair => pair.submeshIndex, static pair => pair.material);

            foreach (ResoniteMeshSubmesh submesh in cityObject.Mesh.Submeshes.OrderBy(static candidate => candidate.Index))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!materialBySubmeshIndex.TryGetValue(submesh.Index, out ResoniteMaterialBinding? material))
                {
                    continue;
                }

                MaterialAtlasTile tile = await CreateAtlasTileAsync(material, cancellationToken);
                entries.Add(new AtlasBatchEntry(cityObject, submesh, material, tile));
            }
        }

        return entries;
    }

    private async Task<MaterialAtlasTile> CreateAtlasTileAsync(
        ResoniteMaterialBinding material,
        CancellationToken cancellationToken)
    {
        if (material.MaterialType == ResoniteMaterialType.VertexColor)
        {
            return CreateSolidColorTile(material.BaseColor);
        }

        if (string.IsNullOrWhiteSpace(material.TexturePath))
        {
            return CreateSolidColorTile(material.BaseColor);
        }

        using Image<Rgba32> sourceImage = await textureImageLoader.LoadAsync(
            material.TexturePath,
            material.TextureSourceKind,
            cancellationToken);

        int targetWidth = Math.Max(1, Math.Min(maxAtlasSize - (tilePaddingPixels * 2), sourceImage.Width));
        int targetHeight = Math.Max(1, Math.Min(maxAtlasSize - (tilePaddingPixels * 2), sourceImage.Height));
        using Image<Rgba32> resizedImage = (sourceImage.Width == targetWidth && sourceImage.Height == targetHeight)
            ? sourceImage.Clone()
            : sourceImage.Clone(context => context.Resize(targetWidth, targetHeight));

        ApplyBaseColor(resizedImage, material.BaseColor);
        return new MaterialAtlasTile(material.TexturePath!, resizedImage.Clone());
    }

    private static MaterialAtlasTile CreateSolidColorTile(ResoniteColor color)
    {
        using Image<Rgba32> image = new(1, 1, ToPixel(color));
        return new MaterialAtlasTile(
            $"solid:{color.R:0.###},{color.G:0.###},{color.B:0.###},{color.A:0.###}",
            image.Clone());
    }

    private List<IReadOnlyList<AtlasBatchEntry>> BuildAtlasEntryBatches(
        IReadOnlyList<AtlasBatchEntry> entries)
    {
        List<IReadOnlyList<AtlasBatchEntry>> batches = [];
        List<AtlasBatchEntry> pending = entries
            .OrderByDescending(static entry => entry.Tile.Image.Height)
            .ThenByDescending(static entry => entry.Tile.Image.Width)
            .ThenBy(static entry => entry.CityObject.SlotKey, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Submesh.Index)
            .ToList();

        while (pending.Count > 0)
        {
            List<AtlasBatchEntry> currentBatch = [];
            foreach (AtlasBatchEntry entry in pending.ToArray())
            {
                List<AtlasBatchEntry> candidateBatch = [.. currentBatch, entry];
                if (TryCreateAtlasLayout(candidateBatch, out _))
                {
                    currentBatch.Add(entry);
                    pending.Remove(entry);
                }
            }

            if (currentBatch.Count == 0)
            {
                throw new InvalidOperationException("Failed to fit LOD2 atlas entries within the configured atlas size.");
            }

            batches.Add(currentBatch);
        }

        return batches;
    }

    private async Task<ResoniteConstructionCityObject> BakeBatchAsync(
        SourceUnitKey sourceUnitKey,
        IReadOnlyList<ResoniteConstructionCityObject> sourceCityObjects,
        IReadOnlyList<AtlasBatchEntry> entries,
        int batchIndex,
        int batchCount,
        CancellationToken cancellationToken)
    {
        if (!TryCreateAtlasLayout(entries, out AtlasLayout? layout) || layout is null)
        {
            throw new InvalidOperationException("Failed to create LOD2 atlas layout.");
        }

        using Image<Rgba32> atlasImage = new(layout.Width, layout.Height, new Rgba32(255, 255, 255, 255));
        foreach (AtlasPlacement placement in layout.Placements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DrawAtlasTile(atlasImage, placement);
        }

        string texturePath = CreateAtlasTexturePath(sourceUnitKey, batchIndex);
        textureImportRegistry.Register(
            texturePath,
            ResoniteTextureSourceKind.Dataset,
            ResoniteTextureImportFactory.CreateRawFromImage(atlasImage, identity: texturePath));

        ResoniteFloat3 bakeOrigin = ComputeBakeOrigin(entries);
        List<ResoniteMeshVertex> vertices = [];
        List<int> triangleIndices = [];

        foreach (AtlasPlacement placement in layout.Placements.OrderBy(static candidate => candidate.Entry.CityObject.SlotKey, StringComparer.Ordinal).ThenBy(static candidate => candidate.Entry.Submesh.Index))
        {
            AppendPlacementGeometry(vertices, triangleIndices, bakeOrigin, placement, layout.Width, layout.Height);
        }

        ResoniteConstructionCityObject firstCityObject = sourceCityObjects[0];
        bool preservePrimaryIdentity = batchCount == 1
            && sourceCityObjects.Count == 1
            && entries.All(entry => ReferenceEquals(entry.CityObject, firstCityObject));
        string slotKey = preservePrimaryIdentity
            ? firstCityObject.SlotKey
            : CreateBatchSlotKey(sourceUnitKey, batchIndex);
        string displayName = preservePrimaryIdentity
            ? firstCityObject.DisplayName
            : CreateBatchDisplayName(sourceUnitKey, batchIndex);
        string? sourceObjectKey = preservePrimaryIdentity
            ? firstCityObject.SourceObjectKey
            : CreateBatchSourceObjectKey(sourceUnitKey, batchIndex);
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: displayName,
            PackageName: firstCityObject.PackageName,
            ActualMeshCode: firstCityObject.ActualMeshCode,
            LodLevel: firstCityObject.LodLevel,
            Transform: new ResoniteTransform(bakeOrigin),
            Mesh: new ResoniteImportedMesh(
                vertices,
                [new ResoniteMeshSubmesh(0, slotKey, triangleIndices)]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: slotKey,
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: texturePath,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0])
            ],
            CollisionEnabled: sourceCityObjects.Any(static cityObject => cityObject.CollisionEnabled),
            SourceObjectKey: sourceObjectKey,
            SourceUnitKey: sourceUnitKey.SourceUnitIdentity);
    }

    private static void AppendPlacementGeometry(
        List<ResoniteMeshVertex> vertices,
        List<int> triangleIndices,
        ResoniteFloat3 bakeOrigin,
        AtlasPlacement placement,
        int atlasWidth,
        int atlasHeight)
    {
        IReadOnlyList<ResoniteMeshVertex> sourceVertices = placement.Entry.CityObject.Mesh.Vertices;
        ResoniteFloat3 cityObjectOffset = Subtract(placement.Entry.CityObject.Transform.Position, bakeOrigin);
        Rect innerRect = placement.InnerRect;

        foreach (int sourceIndex in placement.Entry.Submesh.TriangleVertexIndices)
        {
            ResoniteMeshVertex sourceVertex = sourceVertices[sourceIndex];
            ResoniteFloat2 sourceUv = ApplyMaterialUvTransform(sourceVertex.UV0, placement.Entry.Material);
            ResoniteFloat2 atlasUv = MapUvToAtlas(sourceUv, innerRect, atlasWidth, atlasHeight);
            vertices.Add(sourceVertex with
            {
                Position = Add(sourceVertex.Position, cityObjectOffset),
                UV0 = atlasUv,
            });
            triangleIndices.Add(vertices.Count - 1);
        }
    }

    private static ResoniteFloat2 MapUvToAtlas(
        ResoniteFloat2 sourceUv,
        Rect atlasRect,
        double atlasWidth,
        double atlasHeight)
    {
        double clampedU = Math.Clamp(sourceUv.X, 0.0, 1.0);
        double clampedV = Math.Clamp(sourceUv.Y, 0.0, 1.0);
        return new ResoniteFloat2(
            (atlasRect.X + (clampedU * atlasRect.Width)) / atlasWidth,
            (atlasRect.Y + (clampedV * atlasRect.Height)) / atlasHeight);
    }

    private static ResoniteFloat2 ApplyMaterialUvTransform(
        ResoniteFloat2 sourceUv,
        ResoniteMaterialBinding material)
    {
        double scaleX = material.TextureScale?.X ?? 1.0;
        double scaleY = material.TextureScale?.Y ?? 1.0;
        double offsetX = material.TextureOffset?.X ?? 0.0;
        double offsetY = material.TextureOffset?.Y ?? 0.0;
        return new ResoniteFloat2(
            (sourceUv.X * scaleX) + offsetX,
            (sourceUv.Y * scaleY) + offsetY);
    }

    private static ResoniteFloat3 ComputeBakeOrigin(IReadOnlyList<AtlasBatchEntry> entries)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;

        foreach (AtlasBatchEntry entry in entries)
        {
            foreach (ResoniteMeshVertex vertex in entry.CityObject.Mesh.Vertices)
            {
                ResoniteFloat3 worldPosition = Add(vertex.Position, entry.CityObject.Transform.Position);
                minX = Math.Min(minX, worldPosition.X);
                minY = Math.Min(minY, worldPosition.Y);
                minZ = Math.Min(minZ, worldPosition.Z);
            }
        }

        return double.IsPositiveInfinity(minX)
            ? new ResoniteFloat3(0.0, 0.0, 0.0)
            : new ResoniteFloat3(minX, minY, minZ);
    }

    private void DrawAtlasTile(Image<Rgba32> atlasImage, AtlasPlacement placement)
    {
        for (int y = 0; y < placement.Entry.Tile.Image.Height; y++)
        {
            for (int x = 0; x < placement.Entry.Tile.Image.Width; x++)
            {
                atlasImage[placement.InnerRect.X + x, placement.InnerRect.Y + y] = placement.Entry.Tile.Image[x, y];
            }
        }

        for (int y = 0; y < placement.Entry.Tile.Image.Height; y++)
        {
            Rgba32 leftEdge = atlasImage[placement.InnerRect.X, placement.InnerRect.Y + y];
            Rgba32 rightEdge = atlasImage[placement.InnerRect.X + placement.InnerRect.Width - 1, placement.InnerRect.Y + y];
            for (int pad = 1; pad <= tilePaddingPixels; pad++)
            {
                atlasImage[placement.InnerRect.X - pad, placement.InnerRect.Y + y] = leftEdge;
                atlasImage[placement.InnerRect.X + placement.InnerRect.Width - 1 + pad, placement.InnerRect.Y + y] = rightEdge;
            }
        }

        int fullWidth = placement.InnerRect.Width + (tilePaddingPixels * 2);
        for (int pad = 1; pad <= tilePaddingPixels; pad++)
        {
            int sourceTopY = placement.InnerRect.Y;
            int sourceBottomY = placement.InnerRect.Y + placement.InnerRect.Height - 1;
            int targetTopY = placement.InnerRect.Y - pad;
            int targetBottomY = placement.InnerRect.Y + placement.InnerRect.Height - 1 + pad;
            for (int x = 0; x < fullWidth; x++)
            {
                int sampleX = placement.InnerRect.X - tilePaddingPixels + x;
                atlasImage[sampleX, targetTopY] = atlasImage[sampleX, sourceTopY];
                atlasImage[sampleX, targetBottomY] = atlasImage[sampleX, sourceBottomY];
            }
        }
    }

    private bool TryCreateAtlasLayout(
        IReadOnlyList<AtlasBatchEntry> entries,
        out AtlasLayout? layout)
    {
        List<AtlasPlacement> placements = [];
        int cursorX = 0;
        int cursorY = 0;
        int rowHeight = 0;
        int usedWidth = 0;

        foreach (AtlasBatchEntry entry in entries)
        {
            int paddedWidth = entry.Tile.Image.Width + (tilePaddingPixels * 2);
            int paddedHeight = entry.Tile.Image.Height + (tilePaddingPixels * 2);
            if (paddedWidth > maxAtlasSize || paddedHeight > maxAtlasSize)
            {
                layout = null;
                return false;
            }

            if (cursorX > 0 && cursorX + paddedWidth > maxAtlasSize)
            {
                cursorX = 0;
                cursorY += rowHeight;
                rowHeight = 0;
            }

            if (cursorY + paddedHeight > maxAtlasSize)
            {
                layout = null;
                return false;
            }

            Rect outerRect = new(cursorX, cursorY, paddedWidth, paddedHeight);
            Rect innerRect = new(cursorX + tilePaddingPixels, cursorY + tilePaddingPixels, entry.Tile.Image.Width, entry.Tile.Image.Height);
            placements.Add(new AtlasPlacement(entry, outerRect, innerRect));
            cursorX += paddedWidth;
            rowHeight = Math.Max(rowHeight, paddedHeight);
            usedWidth = Math.Max(usedWidth, cursorX);
        }

        int usedHeight = cursorY + rowHeight;
        layout = new AtlasLayout(
            Math.Max(1, usedWidth),
            Math.Max(1, usedHeight),
            placements);
        return true;
    }

    private static void ApplyBaseColor(Image<Rgba32> image, ResoniteColor color)
    {
        Rgba32 tint = ToPixel(color);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgba32 pixel = image[x, y];
                image[x, y] = new Rgba32(
                    MultiplyChannel(pixel.R, tint.R),
                    MultiplyChannel(pixel.G, tint.G),
                    MultiplyChannel(pixel.B, tint.B),
                    MultiplyChannel(pixel.A, tint.A));
            }
        }
    }

    private static byte MultiplyChannel(byte left, byte right)
    {
        return (byte)Math.Clamp((left * right + 127) / 255, 0, 255);
    }

    private static Rgba32 ToPixel(ResoniteColor color)
    {
        return new Rgba32(
            (byte)Math.Round(Math.Clamp(color.R, 0.0, 1.0) * 255.0),
            (byte)Math.Round(Math.Clamp(color.G, 0.0, 1.0) * 255.0),
            (byte)Math.Round(Math.Clamp(color.B, 0.0, 1.0) * 255.0),
            (byte)Math.Round(Math.Clamp(color.A, 0.0, 1.0) * 255.0));
    }

    private static SourceUnitKey CreateSourceUnitKey(ResoniteConstructionCityObject cityObject)
    {
        string sourceUnitIdentity = cityObject.SourceUnitKey ?? cityObject.SourceObjectKey ?? cityObject.SlotKey;
        return new SourceUnitKey(
            cityObject.ActualMeshCode,
            cityObject.PackageName,
            cityObject.LodLevel,
            sourceUnitIdentity);
    }

    private static string CreateBatchSlotKey(SourceUnitKey sourceUnitKey, int batchIndex)
    {
        string lodToken = sourceUnitKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"atlasbake_{sourceUnitKey.PackageName}_{sourceUnitKey.ActualMeshCode}_{CreateSourceUnitToken(sourceUnitKey.SourceUnitIdentity)}_{lodToken}_{batchIndex:D4}");
    }

    private static string CreateBatchDisplayName(SourceUnitKey sourceUnitKey, int batchIndex)
    {
        string lodToken = sourceUnitKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"AtlasBake {sourceUnitKey.PackageName} LOD{lodToken} {CreateSourceUnitToken(sourceUnitKey.SourceUnitIdentity)} #{batchIndex + 1}");
    }

    private static string CreateBatchSourceObjectKey(SourceUnitKey sourceUnitKey, int batchIndex)
    {
        string lodToken = sourceUnitKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"atlasbake:{sourceUnitKey.ActualMeshCode}:{sourceUnitKey.PackageName}:{CreateSourceUnitToken(sourceUnitKey.SourceUnitIdentity)}:{lodToken}:{batchIndex:D4}");
    }

    private static string CreateAtlasTexturePath(SourceUnitKey sourceUnitKey, int batchIndex)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"generated/lod2-atlas/{sourceUnitKey.ActualMeshCode}/{CreateSourceUnitToken(sourceUnitKey.SourceUnitIdentity)}_{batchIndex:D4}.png");
    }

    private static string CreateSourceUnitToken(string sourceUnitKey)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sourceUnitKey));
        return Convert.ToHexString(bytes.AsSpan(0, 6)).ToLowerInvariant();
    }

    private static ResoniteFloat3 Add(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    private static ResoniteFloat3 Subtract(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }

    private sealed record MaterialAtlasTile(string Identity, Image<Rgba32> Image);

    private sealed record AtlasBatchEntry(
        ResoniteConstructionCityObject CityObject,
        ResoniteMeshSubmesh Submesh,
        ResoniteMaterialBinding Material,
        MaterialAtlasTile Tile);

    private sealed record AtlasLayout(
        int Width,
        int Height,
        IReadOnlyList<AtlasPlacement> Placements);

    private sealed record AtlasPlacement(
        AtlasBatchEntry Entry,
        Rect OuterRect,
        Rect InnerRect);

    private readonly record struct Rect(int X, int Y, int Width, int Height);

    private readonly record struct SourceUnitKey(
        string ActualMeshCode,
        string PackageName,
        int? LodLevel,
        string SourceUnitIdentity);

    private sealed class SourceUnitKeyComparer : IComparer<SourceUnitKey>
    {
        internal static readonly SourceUnitKeyComparer Instance = new();

        public int Compare(SourceUnitKey x, SourceUnitKey y)
        {
            int compare = string.CompareOrdinal(x.ActualMeshCode, y.ActualMeshCode);
            if (compare != 0)
            {
                return compare;
            }

            compare = string.CompareOrdinal(x.PackageName, y.PackageName);
            if (compare != 0)
            {
                return compare;
            }

            compare = Nullable.Compare(x.LodLevel, y.LodLevel);
            if (compare != 0)
            {
                return compare;
            }

            return string.CompareOrdinal(x.SourceUnitIdentity, y.SourceUnitIdentity);
        }
    }
}
