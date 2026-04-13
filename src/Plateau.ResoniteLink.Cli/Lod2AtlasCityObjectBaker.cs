using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Cli;

internal sealed class Lod2AtlasCityObjectBaker(
    ResoniteTextureImageLoader textureImageLoader,
    ResoniteTextureImportRegistry textureImportRegistry,
    int maxAtlasSize = 2048,
    int tilePaddingPixels = 2,
    IReadOnlyList<Lod2AtlasCityObjectBakePolicy>? bakePolicies = null) : IResoniteBufferedCityObjectBaker
{
    internal const int DefaultMaxAtlasSize = 2048;
    internal const int DefaultTilePaddingPixels = 2;

    private readonly Dictionary<SourceUnitKey, List<BufferedCityObject>> bufferedCityObjectsBySourceUnit = [];
    private readonly IReadOnlyList<Lod2AtlasCityObjectBakePolicy> bakePolicies = bakePolicies
        ?? Lod2AtlasCityObjectBakePolicies.DefaultPolicies;

    public string Name => "LOD2AtlasBake";

    public int BakedInputCityObjectCount { get; private set; }

    public int BakedOutputCityObjectCount { get; private set; }

    public ValueTask<bool> TryBufferAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        Lod2AtlasCityObjectBakePolicy? policy = ResolvePolicy(cityObject);
        if (policy is null)
        {
            return ValueTask.FromResult(false);
        }

        SourceUnitKey sourceUnitKey = CreateSourceUnitKey(cityObject, policy);
        BufferedCityObject bufferedCityObject = new(cityObject, policy);
        if (!bufferedCityObjectsBySourceUnit.TryGetValue(sourceUnitKey, out List<BufferedCityObject>? bufferedCityObjects))
        {
            bufferedCityObjects = [];
            bufferedCityObjectsBySourceUnit.Add(sourceUnitKey, bufferedCityObjects);
        }

        bufferedCityObjects.Add(bufferedCityObject);
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
        foreach ((SourceUnitKey sourceUnitKey, List<BufferedCityObject> cityObjects) in bufferedCityObjectsBySourceUnit
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

    private Lod2AtlasCityObjectBakePolicy? ResolvePolicy(ResoniteConstructionCityObject cityObject)
    {
        foreach (Lod2AtlasCityObjectBakePolicy policy in bakePolicies)
        {
            if (policy.CanBuffer(cityObject) && CanBufferCityObjectMaterials(cityObject, policy))
            {
                return policy;
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<ResoniteConstructionCityObject>> BakeSourceUnitAsync(
        SourceUnitKey sourceUnitKey,
        IReadOnlyList<BufferedCityObject> cityObjects,
        CancellationToken cancellationToken)
    {
        List<CityObjectBakeCandidate> candidates = await CreateCandidatesAsync(cityObjects, cancellationToken);
        if (candidates.Count == 0)
        {
            return [];
        }

        try
        {
            List<CityObjectBakeCandidate> passThroughCandidates = [.. candidates.Where(static candidate => candidate.AtlasEntries.Count == 0)];
            List<CityObjectBakeCandidate> atlasCandidates = [.. candidates.Where(static candidate => candidate.AtlasEntries.Count > 0)];

            List<ResoniteConstructionCityObject> bakedCityObjects = [];
            if (passThroughCandidates.Count == 1)
            {
                bakedCityObjects.Add(passThroughCandidates[0].CityObject);
            }
            else if (passThroughCandidates.Count > 1)
            {
                ResoniteConstructionCityObject mergedPassThroughCityObject = await BakeBatchAsync(
                    sourceUnitKey,
                    passThroughCandidates,
                    batchIndex: 0,
                    batchCount: 1,
                    cancellationToken);
                bakedCityObjects.Add(mergedPassThroughCityObject);
            }

            AtlasBatchPlan batchPlan = BuildAtlasCandidateBatches(atlasCandidates);
            for (int batchIndex = 0; batchIndex < batchPlan.Batches.Count; batchIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ResoniteConstructionCityObject bakedCityObject = await BakeBatchAsync(
                    sourceUnitKey,
                    batchPlan.Batches[batchIndex],
                    batchIndex,
                    batchPlan.Batches.Count,
                    cancellationToken);
                bakedCityObjects.Add(bakedCityObject);
            }

            bakedCityObjects.AddRange(batchPlan.FallbackCandidates.Select(static candidate => candidate.CityObject));
            BakedOutputCityObjectCount += bakedCityObjects.Count;
            return bakedCityObjects;
        }
        finally
        {
            foreach (Image<Rgba32> tileImage in candidates
                         .SelectMany(static candidate => candidate.AtlasEntries)
                         .Select(static entry => entry.Tile.Image)
                         .Distinct())
            {
                tileImage.Dispose();
            }
        }
    }

    private async Task<List<CityObjectBakeCandidate>> CreateCandidatesAsync(
        IReadOnlyList<BufferedCityObject> cityObjects,
        CancellationToken cancellationToken)
    {
        List<CityObjectBakeCandidate> candidates = [];

        foreach (BufferedCityObject bufferedCityObject in cityObjects.OrderBy(
                     static bufferedCityObject => bufferedCityObject.CityObject.SlotKey,
                     StringComparer.Ordinal))
        {
            ResoniteConstructionCityObject cityObject = bufferedCityObject.CityObject;
            Lod2AtlasCityObjectBakePolicy policy = bufferedCityObject.Policy;
            if (!TryCreateMaterialBySubmeshIndex(cityObject, out Dictionary<int, ResoniteMaterialBinding> materialBySubmeshIndex))
            {
                continue;
            }

            List<AtlasBatchEntry> atlasEntries = [];
            List<PreservedSubmeshEntry> preservedEntries = [];
            foreach (ResoniteMeshSubmesh submesh in cityObject.Mesh.Submeshes.OrderBy(static candidate => candidate.Index))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!materialBySubmeshIndex.TryGetValue(submesh.Index, out ResoniteMaterialBinding? material))
                {
                    continue;
                }

                Lod2AtlasMaterialBakeCategory category = ClassifyMaterial(material);
                switch (category)
                {
                    case Lod2AtlasMaterialBakeCategory.AtlasCandidate:
                        UvBounds uvBounds = ComputeUvBounds(cityObject.Mesh.Vertices, submesh, material);
                        MaterialAtlasTile tile = await CreateAtlasTileAsync(material, uvBounds, cancellationToken);
                        atlasEntries.Add(new AtlasBatchEntry(cityObject, submesh, material, tile, uvBounds));
                        break;
                    case Lod2AtlasMaterialBakeCategory.PreservedCommonMaterial when policy.PreserveCommonMaterials:
                    case Lod2AtlasMaterialBakeCategory.PreservedTextureless when policy.PreserveTexturelessMaterials:
                    case Lod2AtlasMaterialBakeCategory.PreservedVertexColor when policy.PreserveVertexColorMaterials:
                    case Lod2AtlasMaterialBakeCategory.PreservedOther:
                        preservedEntries.Add(new PreservedSubmeshEntry(cityObject, submesh, material));
                        break;
                }
            }

            if (policy.RequireAtlasCandidateMaterial && atlasEntries.Count == 0)
            {
                continue;
            }

            if (atlasEntries.Count > 0 || preservedEntries.Count > 0)
            {
                candidates.Add(new CityObjectBakeCandidate(cityObject, atlasEntries, preservedEntries));
            }
        }

        return candidates;
    }

    private async Task<MaterialAtlasTile> CreateAtlasTileAsync(
        ResoniteMaterialBinding material,
        UvBounds uvBounds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(material.TexturePath))
        {
            return CreateSolidColorTile(material.BaseColor);
        }

        using Image<Rgba32> sourceImage = await textureImageLoader.LoadAsync(
            material.TexturePath,
            material.TextureSourceKind,
            cancellationToken);

        int maxTileWidth = Math.Max(1, maxAtlasSize - (tilePaddingPixels * 2));
        int maxTileHeight = Math.Max(1, maxAtlasSize - (tilePaddingPixels * 2));
        int targetWidth = Math.Max(1, Math.Min(maxTileWidth, (int)Math.Ceiling(sourceImage.Width * uvBounds.Width)));
        int targetHeight = Math.Max(1, Math.Min(maxTileHeight, (int)Math.Ceiling(sourceImage.Height * uvBounds.Height)));
        using Image<Rgba32> bakedImage = BakeUsedUvRegion(sourceImage, uvBounds, targetWidth, targetHeight);

        ApplyBaseColor(bakedImage, material.BaseColor);
        return new MaterialAtlasTile(material.TexturePath!, bakedImage.Clone());
    }

    private static MaterialAtlasTile CreateSolidColorTile(ResoniteColor color)
    {
        using Image<Rgba32> image = new(1, 1, ToPixel(color));
        return new MaterialAtlasTile(
            $"solid:{color.R:0.###},{color.G:0.###},{color.B:0.###},{color.A:0.###}",
            image.Clone());
    }

    private static bool CanBufferCityObjectMaterials(
        ResoniteConstructionCityObject cityObject,
        Lod2AtlasCityObjectBakePolicy policy)
    {
        if (!TryCreateMaterialBySubmeshIndex(cityObject, out Dictionary<int, ResoniteMaterialBinding> materialBySubmeshIndex))
        {
            return false;
        }

        bool hasAtlasCandidateSubmesh = false;
        foreach (ResoniteMeshSubmesh submesh in cityObject.Mesh.Submeshes)
        {
            if (!materialBySubmeshIndex.TryGetValue(submesh.Index, out ResoniteMaterialBinding? material))
            {
                return false;
            }

            Lod2AtlasMaterialBakeCategory category = ClassifyMaterial(material);
            hasAtlasCandidateSubmesh |= category == Lod2AtlasMaterialBakeCategory.AtlasCandidate;
            if (category == Lod2AtlasMaterialBakeCategory.PreservedCommonMaterial && !policy.PreserveCommonMaterials)
            {
                return false;
            }

            if (category == Lod2AtlasMaterialBakeCategory.PreservedVertexColor && !policy.PreserveVertexColorMaterials)
            {
                return false;
            }

            if (category == Lod2AtlasMaterialBakeCategory.PreservedTextureless && !policy.PreserveTexturelessMaterials)
            {
                return false;
            }
        }

        return policy.RequireAtlasCandidateMaterial ? hasAtlasCandidateSubmesh : true;
    }

    private static bool TryCreateMaterialBySubmeshIndex(
        ResoniteConstructionCityObject cityObject,
        out Dictionary<int, ResoniteMaterialBinding> materialBySubmeshIndex)
    {
        materialBySubmeshIndex = [];
        foreach (ResoniteMaterialBinding material in cityObject.Materials)
        {
            foreach (int submeshIndex in material.SubmeshIndices)
            {
                if (!materialBySubmeshIndex.TryAdd(submeshIndex, material))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsAtlasBakeCandidate(ResoniteMaterialBinding material)
    {
        if (material.DepthOffset is not null
            || !string.IsNullOrWhiteSpace(material.Family)
            || material.Projection != ResoniteMaterialProjection.Uv
            || material.AssetScope == ResoniteMaterialAssetScope.Common)
        {
            return false;
        }

        return material.MaterialType == ResoniteMaterialType.Standard
            && !string.IsNullOrWhiteSpace(material.TexturePath)
            && material.TextureSourceKind == ResoniteTextureSourceKind.Dataset;
    }

    private static Lod2AtlasMaterialBakeCategory ClassifyMaterial(ResoniteMaterialBinding material)
    {
        if (material.MaterialType == ResoniteMaterialType.VertexColor)
        {
            return Lod2AtlasMaterialBakeCategory.PreservedVertexColor;
        }

        if (material.AssetScope == ResoniteMaterialAssetScope.Common
            || !string.IsNullOrWhiteSpace(material.Family))
        {
            return Lod2AtlasMaterialBakeCategory.PreservedCommonMaterial;
        }

        if (IsAtlasBakeCandidate(material))
        {
            return Lod2AtlasMaterialBakeCategory.AtlasCandidate;
        }

        if (string.IsNullOrWhiteSpace(material.TexturePath))
        {
            return Lod2AtlasMaterialBakeCategory.PreservedTextureless;
        }

        return Lod2AtlasMaterialBakeCategory.PreservedOther;
    }

    private AtlasBatchPlan BuildAtlasCandidateBatches(
        IReadOnlyList<CityObjectBakeCandidate> candidates)
    {
        List<IReadOnlyList<CityObjectBakeCandidate>> batches = [];
        List<CityObjectBakeCandidate> fallbackCandidates = [];
        List<CityObjectBakeCandidate> pending = candidates
            .OrderByDescending(static candidate => candidate.AtlasEntries.Sum(entry => entry.Tile.Image.Width * entry.Tile.Image.Height))
            .ThenBy(static candidate => candidate.CityObject.SlotKey, StringComparer.Ordinal)
            .ToList();

        while (pending.Count > 0)
        {
            List<CityObjectBakeCandidate> currentBatch = [];
            foreach (CityObjectBakeCandidate candidate in pending.ToArray())
            {
                List<AtlasBatchEntry> candidateEntries = [.. currentBatch.SelectMany(static current => current.AtlasEntries), .. candidate.AtlasEntries];
                if (TryCreateAtlasLayout(candidateEntries, out _))
                {
                    currentBatch.Add(candidate);
                    pending.Remove(candidate);
                }
            }

            if (currentBatch.Count == 0)
            {
                CityObjectBakeCandidate oversizedCandidate = pending[0];
                pending.RemoveAt(0);
                fallbackCandidates.Add(oversizedCandidate);
                continue;
            }

            batches.Add(currentBatch);
        }

        return new AtlasBatchPlan(batches, fallbackCandidates);
    }

    private async Task<ResoniteConstructionCityObject> BakeBatchAsync(
        SourceUnitKey sourceUnitKey,
        IReadOnlyList<CityObjectBakeCandidate> candidates,
        int batchIndex,
        int batchCount,
        CancellationToken cancellationToken)
    {
        List<AtlasBatchEntry> entries = candidates.SelectMany(static candidate => candidate.AtlasEntries).ToList();
        AtlasLayout? layout = null;
        if (entries.Count > 0
            && (!TryCreateAtlasLayout(entries, out layout) || layout is null))
        {
            throw new InvalidOperationException("Failed to create LOD2 atlas layout.");
        }

        using Image<Rgba32>? atlasImage = layout is null
            ? null
            : new Image<Rgba32>(layout.Width, layout.Height, new Rgba32(255, 255, 255, 255));
        if (layout is not null)
        {
            foreach (AtlasPlacement placement in layout.Placements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DrawAtlasTile(atlasImage!, placement);
            }
        }

        ResoniteConstructionCityObject firstCityObject = candidates[0].CityObject;
        bool preservePrimaryIdentity = batchCount == 1
            && candidates.Count == 1;
        string slotKey = preservePrimaryIdentity
            ? firstCityObject.SlotKey
            : CreateBatchSlotKey(sourceUnitKey, batchIndex);
        string displayName = preservePrimaryIdentity
            ? firstCityObject.DisplayName
            : CreateBatchDisplayName(sourceUnitKey, batchIndex);
        string? sourceObjectKey = preservePrimaryIdentity
            ? firstCityObject.SourceObjectKey
            : CreateBatchSourceObjectKey(sourceUnitKey, batchIndex);

        ResoniteFloat3 bakeOrigin = ComputeBakeOrigin(candidates);
        List<ResoniteMeshVertex> vertices = [];
        List<ResoniteMeshSubmesh> submeshes = [];
        List<ResoniteMaterialBinding> materials = [];

        if (layout is not null)
        {
            string texturePath = CreateAtlasTexturePath(sourceUnitKey, batchIndex);
            textureImportRegistry.Register(
                texturePath,
                ResoniteTextureSourceKind.Dataset,
                ResoniteTextureImportFactory.CreateRawFromImage(atlasImage!, identity: texturePath));

            List<int> atlasTriangleIndices = [];
            foreach (AtlasPlacement placement in layout.Placements.OrderBy(static candidate => candidate.Entry.CityObject.SlotKey, StringComparer.Ordinal).ThenBy(static candidate => candidate.Entry.Submesh.Index))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendPlacementGeometry(vertices, atlasTriangleIndices, bakeOrigin, placement, layout.Width, layout.Height);
            }

            string atlasMaterialKey = string.Create(CultureInfo.InvariantCulture, $"{slotKey}_atlas");
            submeshes.Add(new ResoniteMeshSubmesh(0, atlasMaterialKey, atlasTriangleIndices));
            materials.Add(
                new ResoniteMaterialBinding(
                    MaterialKey: atlasMaterialKey,
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: texturePath,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]));
        }

        foreach (IGrouping<PreservedMaterialGroupingKey, PreservedSubmeshEntry> preservedGroup in candidates
                     .SelectMany(static candidate => candidate.PreservedEntries)
                     .GroupBy(static entry => CreatePreservedMaterialGroupingKey(entry.Material), PreservedMaterialGroupingKeyComparer.Instance)
                     .OrderBy(static group => group.Key.MaterialKey, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<int> preservedTriangleIndices = [];
            foreach (PreservedSubmeshEntry preservedEntry in preservedGroup
                         .OrderBy(static entry => entry.CityObject.SlotKey, StringComparer.Ordinal)
                         .ThenBy(static entry => entry.Submesh.Index))
            {
                AppendOriginalGeometry(vertices, preservedTriangleIndices, bakeOrigin, preservedEntry);
            }

            if (preservedTriangleIndices.Count == 0)
            {
                continue;
            }

            int submeshIndex = submeshes.Count;
            ResoniteMaterialBinding preservedMaterial = NormalizePreservedMaterial(preservedGroup.First().Material) with
            {
                SubmeshIndices = [submeshIndex],
            };
            submeshes.Add(new ResoniteMeshSubmesh(submeshIndex, preservedMaterial.MaterialKey, preservedTriangleIndices));
            materials.Add(preservedMaterial);
        }

        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: displayName,
            PackageName: firstCityObject.PackageName,
            ActualMeshCode: firstCityObject.ActualMeshCode,
            LodLevel: firstCityObject.LodLevel,
            Transform: new ResoniteTransform(bakeOrigin),
            Mesh: new ResoniteImportedMesh(vertices, submeshes),
            Materials: materials,
            CollisionEnabled: candidates.Any(static candidate => candidate.CityObject.CollisionEnabled),
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
            ResoniteFloat2 atlasUv = MapUvToAtlas(sourceUv, placement.Entry.UvBounds, innerRect, atlasWidth, atlasHeight);
            vertices.Add(sourceVertex with
            {
                Position = Add(sourceVertex.Position, cityObjectOffset),
                UV0 = atlasUv,
            });
            triangleIndices.Add(vertices.Count - 1);
        }
    }

    private static void AppendOriginalGeometry(
        List<ResoniteMeshVertex> vertices,
        List<int> triangleIndices,
        ResoniteFloat3 bakeOrigin,
        PreservedSubmeshEntry preservedEntry)
    {
        IReadOnlyList<ResoniteMeshVertex> sourceVertices = preservedEntry.CityObject.Mesh.Vertices;
        ResoniteFloat3 cityObjectOffset = Subtract(preservedEntry.CityObject.Transform.Position, bakeOrigin);
        foreach (int sourceIndex in preservedEntry.Submesh.TriangleVertexIndices)
        {
            ResoniteMeshVertex sourceVertex = sourceVertices[sourceIndex];
            vertices.Add(sourceVertex with
            {
                Position = Add(sourceVertex.Position, cityObjectOffset),
            });
            triangleIndices.Add(vertices.Count - 1);
        }
    }

    private static ResoniteFloat2 MapUvToAtlas(
        ResoniteFloat2 sourceUv,
        UvBounds uvBounds,
        Rect atlasRect,
        double atlasWidth,
        double atlasHeight)
    {
        double normalizedU = NormalizeToBounds(sourceUv.X, uvBounds.MinU, uvBounds.Width);
        double normalizedV = NormalizeToBounds(sourceUv.Y, uvBounds.MinV, uvBounds.Height);
        return new ResoniteFloat2(
            (atlasRect.X + (normalizedU * atlasRect.Width)) / atlasWidth,
            (atlasHeight - atlasRect.Y - atlasRect.Height + (normalizedV * atlasRect.Height)) / atlasHeight);
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

    private static ResoniteFloat3 ComputeBakeOrigin(IReadOnlyList<CityObjectBakeCandidate> candidates)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;

        foreach (ResoniteConstructionCityObject cityObject in candidates.Select(static candidate => candidate.CityObject))
        {
            foreach (ResoniteMeshVertex vertex in cityObject.Mesh.Vertices)
            {
                ResoniteFloat3 worldPosition = Add(vertex.Position, cityObject.Transform.Position);
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

    private static SourceUnitKey CreateSourceUnitKey(
        ResoniteConstructionCityObject cityObject,
        Lod2AtlasCityObjectBakePolicy policy)
    {
        string context = policy.Name;
        string sourceUnitIdentity;
        if (policy.EnableGridPassThrough && policy.PassThroughGridCellSizeMeters > 0)
        {
            sourceUnitIdentity = CreateGridCellToken(cityObject, policy.PassThroughGridCellSizeMeters);
        }
        else
        {
            sourceUnitIdentity = cityObject.SourceUnitKey ?? cityObject.SourceObjectKey ?? cityObject.SlotKey;
        }

        return new SourceUnitKey(
            cityObject.ActualMeshCode,
            cityObject.PackageName,
            cityObject.LodLevel,
            sourceUnitIdentity,
            context);
    }

    private static string CreateGridCellToken(
        ResoniteConstructionCityObject cityObject,
        int gridSizeMeters)
    {
        long cellX = (long)Math.Floor(cityObject.Transform.Position.X / gridSizeMeters);
        long cellZ = (long)Math.Floor(cityObject.Transform.Position.Z / gridSizeMeters);
        return $"{cellX}_{cellZ}";
    }

    private static string CreateBatchSlotKey(SourceUnitKey sourceUnitKey, int batchIndex)
    {
        string lodToken = sourceUnitKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        string policyToken = CreatePolicyContextToken(sourceUnitKey.PolicyContext);
        string sourceToken = CreateSourceUnitToken(sourceUnitKey.SourceUnitIdentity);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"atlasbake_{sourceUnitKey.PackageName}_{sourceUnitKey.ActualMeshCode}_{policyToken}_{sourceToken}_{lodToken}_{batchIndex:D4}");
    }

    private static string CreateBatchDisplayName(SourceUnitKey sourceUnitKey, int batchIndex)
    {
        string lodToken = sourceUnitKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"AtlasBake {sourceUnitKey.PackageName} LOD{lodToken} {CreatePolicyContextToken(sourceUnitKey.PolicyContext)}-{CreateSourceUnitToken(sourceUnitKey.SourceUnitIdentity)} #{batchIndex + 1}");
    }

    private static string CreateBatchSourceObjectKey(SourceUnitKey sourceUnitKey, int batchIndex)
    {
        string lodToken = sourceUnitKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"atlasbake:{sourceUnitKey.ActualMeshCode}:{sourceUnitKey.PackageName}:{CreatePolicyContextToken(sourceUnitKey.PolicyContext)}:{CreateSourceUnitToken(sourceUnitKey.SourceUnitIdentity)}:{lodToken}:{batchIndex:D4}");
    }

    private static string CreateAtlasTexturePath(SourceUnitKey sourceUnitKey, int batchIndex)
    {
        string lodToken = sourceUnitKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"generated/lod2-atlas/{sourceUnitKey.ActualMeshCode}/{CreatePolicyContextToken(sourceUnitKey.PolicyContext)}/{sourceUnitKey.PackageName}_{lodToken}_{CreateSourceUnitToken(sourceUnitKey.SourceUnitIdentity)}_{batchIndex:D4}.png");
    }

    private static string CreatePolicyContextToken(string policyContext)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(policyContext));
        return Convert.ToHexString(bytes.AsSpan(0, 4)).ToLowerInvariant();
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

    private static UvBounds ComputeUvBounds(
        IReadOnlyList<ResoniteMeshVertex> vertices,
        ResoniteMeshSubmesh submesh,
        ResoniteMaterialBinding material)
    {
        double minU = double.PositiveInfinity;
        double minV = double.PositiveInfinity;
        double maxU = double.NegativeInfinity;
        double maxV = double.NegativeInfinity;

        foreach (int sourceIndex in submesh.TriangleVertexIndices)
        {
            ResoniteFloat2 transformedUv = ApplyMaterialUvTransform(vertices[sourceIndex].UV0, material);
            minU = Math.Min(minU, transformedUv.X);
            minV = Math.Min(minV, transformedUv.Y);
            maxU = Math.Max(maxU, transformedUv.X);
            maxV = Math.Max(maxV, transformedUv.Y);
        }

        if (double.IsPositiveInfinity(minU) || double.IsPositiveInfinity(minV))
        {
            return new UvBounds(0.0, 0.0, 1.0, 1.0);
        }

        double width = Math.Max(1.0 / 1024.0, maxU - minU);
        double height = Math.Max(1.0 / 1024.0, maxV - minV);
        return new UvBounds(minU, minV, width, height);
    }

    private static Image<Rgba32> BakeUsedUvRegion(
        Image<Rgba32> sourceImage,
        UvBounds uvBounds,
        int targetWidth,
        int targetHeight)
    {
        Image<Rgba32> bakedImage = new(targetWidth, targetHeight);
        for (int y = 0; y < targetHeight; y++)
        {
            double normalizedV = 1.0 - ((y + 0.5) / targetHeight);
            double sourceV = uvBounds.MinV + (normalizedV * uvBounds.Height);
            for (int x = 0; x < targetWidth; x++)
            {
                double normalizedU = (x + 0.5) / targetWidth;
                double sourceU = uvBounds.MinU + (normalizedU * uvBounds.Width);
                bakedImage[x, y] = SampleWrappedPixelBilinear(sourceImage, sourceU, sourceV);
            }
        }

        return bakedImage;
    }

    private static Rgba32 SampleWrappedPixelBilinear(Image<Rgba32> sourceImage, double u, double v)
    {
        double wrappedU = WrapUvCoordinate(u);
        double wrappedV = WrapUvCoordinate(v);
        double sourceX = (wrappedU * sourceImage.Width) - 0.5;
        double sourceY = ((1.0 - wrappedV) * sourceImage.Height) - 0.5;
        int x0 = (int)Math.Floor(sourceX);
        int y0 = (int)Math.Floor(sourceY);
        int x1 = x0 + 1;
        int y1 = y0 + 1;
        double tx = sourceX - x0;
        double ty = sourceY - y0;

        Rgba32 topLeft = sourceImage[WrapPixelCoordinate(x0, sourceImage.Width), WrapPixelCoordinate(y0, sourceImage.Height)];
        Rgba32 topRight = sourceImage[WrapPixelCoordinate(x1, sourceImage.Width), WrapPixelCoordinate(y0, sourceImage.Height)];
        Rgba32 bottomLeft = sourceImage[WrapPixelCoordinate(x0, sourceImage.Width), WrapPixelCoordinate(y1, sourceImage.Height)];
        Rgba32 bottomRight = sourceImage[WrapPixelCoordinate(x1, sourceImage.Width), WrapPixelCoordinate(y1, sourceImage.Height)];
        return LerpPixels(topLeft, topRight, bottomLeft, bottomRight, tx, ty);
    }

    private static double WrapUvCoordinate(double value)
    {
        double wrapped = value - Math.Floor(value);
        return wrapped >= 1.0 ? 0.0 : wrapped;
    }

    private static int WrapPixelCoordinate(int value, int length)
    {
        int wrapped = value % length;
        return wrapped < 0 ? wrapped + length : wrapped;
    }

    private static Rgba32 LerpPixels(
        Rgba32 topLeft,
        Rgba32 topRight,
        Rgba32 bottomLeft,
        Rgba32 bottomRight,
        double tx,
        double ty)
    {
        return new Rgba32(
            LerpChannel(topLeft.R, topRight.R, bottomLeft.R, bottomRight.R, tx, ty),
            LerpChannel(topLeft.G, topRight.G, bottomLeft.G, bottomRight.G, tx, ty),
            LerpChannel(topLeft.B, topRight.B, bottomLeft.B, bottomRight.B, tx, ty),
            LerpChannel(topLeft.A, topRight.A, bottomLeft.A, bottomRight.A, tx, ty));
    }

    private static byte LerpChannel(
        byte topLeft,
        byte topRight,
        byte bottomLeft,
        byte bottomRight,
        double tx,
        double ty)
    {
        double top = topLeft + ((topRight - topLeft) * tx);
        double bottom = bottomLeft + ((bottomRight - bottomLeft) * tx);
        double value = top + ((bottom - top) * ty);
        return (byte)Math.Clamp(Math.Round(value), 0.0, 255.0);
    }

    private static double NormalizeToBounds(double value, double min, double length)
    {
        if (length <= 0.0)
        {
            return 0.0;
        }

        return Math.Clamp((value - min) / length, 0.0, 1.0);
    }

    private static PreservedMaterialGroupingKey CreatePreservedMaterialGroupingKey(ResoniteMaterialBinding material)
    {
        ResoniteMaterialBinding normalizedMaterial = NormalizePreservedMaterial(material);
        return new PreservedMaterialGroupingKey(
            normalizedMaterial.MaterialKey,
            normalizedMaterial.BaseColor,
            normalizedMaterial.MaterialType,
            normalizedMaterial.TexturePath,
            normalizedMaterial.TextureSourceKind,
            normalizedMaterial.Projection,
            normalizedMaterial.DepthOffset,
            normalizedMaterial.TextureScale,
            normalizedMaterial.Family,
            normalizedMaterial.TextureOffset,
            normalizedMaterial.AssetScope);
    }

    private static ResoniteMaterialBinding NormalizePreservedMaterial(ResoniteMaterialBinding material)
    {
        if (material.MaterialType == ResoniteMaterialType.VertexColor)
        {
            return material with
            {
                MaterialKey = "preserved-vertex-color",
            };
        }

        if (material.MaterialType == ResoniteMaterialType.Standard
            && string.IsNullOrWhiteSpace(material.TexturePath)
            && material.AssetScope != ResoniteMaterialAssetScope.Common
            && string.IsNullOrWhiteSpace(material.Family))
        {
            return material with
            {
                MaterialKey = "preserved-standard-textureless",
            };
        }

        if (material.AssetScope != ResoniteMaterialAssetScope.Common
            || (material.Family != BundledDefaultMaterialFamilies.Facade
                && material.Family != BundledDefaultMaterialFamilies.Roof))
        {
            return material;
        }

        string canonicalTexturePath = BundledDefaultMaterialFamilies.GetVariants(material.Family)[0];
        return material with
        {
            MaterialKey = $"common-{material.Family}",
            TexturePath = canonicalTexturePath,
            TextureSourceKind = ResoniteTextureSourceKind.Bundled,
            TextureScale = BundledDefaultMaterialProfiles.GetTilesPerMeter(canonicalTexturePath),
            TextureOffset = null,
        };
    }

    private sealed record MaterialAtlasTile(string Identity, Image<Rgba32> Image);

    private readonly record struct BufferedCityObject(
        ResoniteConstructionCityObject CityObject,
        Lod2AtlasCityObjectBakePolicy Policy);

    private sealed record AtlasBatchEntry(
        ResoniteConstructionCityObject CityObject,
        ResoniteMeshSubmesh Submesh,
        ResoniteMaterialBinding Material,
        MaterialAtlasTile Tile,
        UvBounds UvBounds);

    private sealed record PreservedSubmeshEntry(
        ResoniteConstructionCityObject CityObject,
        ResoniteMeshSubmesh Submesh,
        ResoniteMaterialBinding Material);

    private sealed record CityObjectBakeCandidate(
        ResoniteConstructionCityObject CityObject,
        IReadOnlyList<AtlasBatchEntry> AtlasEntries,
        IReadOnlyList<PreservedSubmeshEntry> PreservedEntries);

    private sealed record AtlasBatchPlan(
        IReadOnlyList<IReadOnlyList<CityObjectBakeCandidate>> Batches,
        IReadOnlyList<CityObjectBakeCandidate> FallbackCandidates);

    private sealed record AtlasLayout(
        int Width,
        int Height,
        IReadOnlyList<AtlasPlacement> Placements);

    private sealed record AtlasPlacement(
        AtlasBatchEntry Entry,
        Rect OuterRect,
        Rect InnerRect);

    private readonly record struct Rect(int X, int Y, int Width, int Height);

    private readonly record struct UvBounds(
        double MinU,
        double MinV,
        double Width,
        double Height);

    private readonly record struct SourceUnitKey(
        string ActualMeshCode,
        string PackageName,
        int? LodLevel,
        string SourceUnitIdentity,
        string PolicyContext);

    private readonly record struct PreservedMaterialGroupingKey(
        string MaterialKey,
        ResoniteColor BaseColor,
        ResoniteMaterialType MaterialType,
        string? TexturePath,
        ResoniteTextureSourceKind TextureSourceKind,
        ResoniteMaterialProjection Projection,
        ResoniteMaterialDepthOffset? DepthOffset,
        ResoniteFloat2? TextureScale,
        string? Family,
        ResoniteFloat2? TextureOffset,
        ResoniteMaterialAssetScope AssetScope);

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

            compare = string.CompareOrdinal(x.PolicyContext, y.PolicyContext);
            if (compare != 0)
            {
                return compare;
            }

            return string.CompareOrdinal(x.SourceUnitIdentity, y.SourceUnitIdentity);
        }
    }

    private sealed class PreservedMaterialGroupingKeyComparer : IEqualityComparer<PreservedMaterialGroupingKey>
    {
        internal static readonly PreservedMaterialGroupingKeyComparer Instance = new();

        public bool Equals(PreservedMaterialGroupingKey x, PreservedMaterialGroupingKey y)
        {
            return string.Equals(x.MaterialKey, y.MaterialKey, StringComparison.Ordinal)
                && x.BaseColor == y.BaseColor
                && x.MaterialType == y.MaterialType
                && string.Equals(x.TexturePath, y.TexturePath, StringComparison.Ordinal)
                && x.TextureSourceKind == y.TextureSourceKind
                && x.Projection == y.Projection
                && EqualityComparer<ResoniteMaterialDepthOffset?>.Default.Equals(x.DepthOffset, y.DepthOffset)
                && EqualityComparer<ResoniteFloat2?>.Default.Equals(x.TextureScale, y.TextureScale)
                && string.Equals(x.Family, y.Family, StringComparison.Ordinal)
                && EqualityComparer<ResoniteFloat2?>.Default.Equals(x.TextureOffset, y.TextureOffset)
                && x.AssetScope == y.AssetScope;
        }

        public int GetHashCode(PreservedMaterialGroupingKey obj)
        {
            HashCode hash = new();
            hash.Add(obj.MaterialKey, StringComparer.Ordinal);
            hash.Add(obj.BaseColor);
            hash.Add(obj.MaterialType);
            hash.Add(obj.TexturePath, StringComparer.Ordinal);
            hash.Add(obj.TextureSourceKind);
            hash.Add(obj.Projection);
            hash.Add(obj.DepthOffset);
            hash.Add(obj.TextureScale);
            hash.Add(obj.Family, StringComparer.Ordinal);
            hash.Add(obj.TextureOffset);
            hash.Add(obj.AssetScope);
            return hash.ToHashCode();
        }
    }
}
