using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Targets.Resonite;

internal sealed class Lod2AtlasCityObjectBaker(
    ResoniteTextureImageLoader textureImageLoader,
    int maxAtlasSize = 4096,
    int tilePaddingPixels = 2,
    IReadOnlyList<Lod2AtlasCityObjectBakePolicy>? bakePolicies = null,
    int maxBufferedSourceUnits = 32,
    int maxBufferedCityObjectsPerSourceUnit = 256,
    ResoniteImportBudgetProfile? resourceBudget = null) : IResoniteBufferedCityObjectBaker
{
    internal const int DefaultMaxAtlasSize = 4096;
    internal const int DefaultTilePaddingPixels = 2;

    private readonly Dictionary<SourceUnitBatchKey, List<BufferedCityObject>> bufferedCityObjectsBySourceUnit = [];
    private readonly Dictionary<SourceUnitBatchKey, int> nextBatchIndexBySourceUnit = [];
    private readonly int maxBufferedSourceUnitsForCompatibility = maxBufferedSourceUnits;
    private readonly int maxBufferedCityObjectsPerSourceUnitForCompatibility = maxBufferedCityObjectsPerSourceUnit;
    private readonly IReadOnlyList<Lod2AtlasCityObjectBakePolicy> bakePolicies = bakePolicies
        ?? Lod2AtlasCityObjectBakePolicies.DefaultPolicies;

    public string Name => "LOD2AtlasBake";

    public int BakedInputCityObjectCount { get; private set; }

    public int BakedOutputCityObjectCount { get; private set; }

    private int EffectiveMaxAtlasSize => Math.Max(1, Math.Min(maxAtlasSize, resourceBudget?.MaxAtlasSize ?? maxAtlasSize));

    private int EffectiveMaxAtlasTextureEdge
    {
        get
        {
            int profileMaxTileEdge = resourceBudget?.MaxAtlasTextureEdge ?? EffectiveMaxAtlasSize;
            return Math.Max(1, Math.Min(EffectiveMaxAtlasSize - (tilePaddingPixels * 2), profileMaxTileEdge));
        }
    }

    public async ValueTask<BufferedCityObjectBufferResult> TryBufferAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        cancellationToken.ThrowIfCancellationRequested();
        _ = maxBufferedSourceUnitsForCompatibility;
        _ = maxBufferedCityObjectsPerSourceUnitForCompatibility;

        Lod2AtlasCityObjectBakePolicy? policy = ResolvePolicy(cityObject);
        if (policy is null)
        {
            return new BufferedCityObjectBufferResult(Buffered: false, []);
        }

        SourceUnitBatchKey sourceUnitKey = CreateSourceUnitKey(cityObject, policy);
        List<ResoniteConstructionCityObject> readyCityObjects = [];
        BufferedCityObject bufferedCityObject = new(cityObject, policy);
        if (!bufferedCityObjectsBySourceUnit.TryGetValue(sourceUnitKey, out List<BufferedCityObject>? bufferedCityObjects))
        {
            bufferedCityObjects = [];
            bufferedCityObjectsBySourceUnit.Add(sourceUnitKey, bufferedCityObjects);
        }

        bufferedCityObjects.Add(bufferedCityObject);
        BakedInputCityObjectCount++;
        return new BufferedCityObjectBufferResult(Buffered: true, readyCityObjects);
    }

    public async Task<IReadOnlyList<ResoniteConstructionCityObject>> FlushAllAsync(
        CancellationToken cancellationToken = default)
    {
        List<ResoniteConstructionCityObject> bakedCityObjects = [];
        await FlushAllAsync(
            (bakedCityObject, _) =>
            {
                bakedCityObjects.Add(bakedCityObject);
                return Task.CompletedTask;
            },
            cancellationToken);
        return bakedCityObjects;
    }

    public async Task FlushAllAsync(
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onBakedCityObject);
        if (bufferedCityObjectsBySourceUnit.Count == 0)
        {
            return;
        }

        SourceUnitBatchKey[] orderedSourceUnitKeys = bufferedCityObjectsBySourceUnit.Keys
            .OrderBy(static key => key, SourceUnitKeyComparer.Instance)
            .ToArray();
        foreach (SourceUnitBatchKey sourceUnitKey in orderedSourceUnitKeys)
        {
            await EmitSourceUnitAsync(sourceUnitKey, onBakedCityObject, cancellationToken);
        }
    }

    private async Task EmitSourceUnitAsync(
        SourceUnitBatchKey sourceUnitKey,
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken)
    {
        if (!bufferedCityObjectsBySourceUnit.Remove(sourceUnitKey, out List<BufferedCityObject>? cityObjects))
        {
            return;
        }

        int emittedCount = 0;
        int batchStartIndex = nextBatchIndexBySourceUnit.GetValueOrDefault(sourceUnitKey);

        await BakeSourceUnitAsync(
            sourceUnitKey,
            cityObjects,
            batchStartIndex,
            (bakedCityObject, callbackCancellationToken) =>
            {
                emittedCount++;
                return onBakedCityObject(bakedCityObject, callbackCancellationToken);
            },
            cancellationToken);

        nextBatchIndexBySourceUnit[sourceUnitKey] = batchStartIndex + emittedCount;
        cityObjects.Clear();
    }

    private async Task BakeSourceUnitAsync(
        SourceUnitBatchKey sourceUnitKey,
        IReadOnlyList<BufferedCityObject> cityObjects,
        int batchStartIndex,
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken)
    {
        List<CityObjectBakeCandidate> passThroughCandidates = [];
        List<CityObjectBakeCandidate> currentAtlasBatch = [];
        int batchIndex = batchStartIndex;
        bool preservePrimaryIdentity = cityObjects.Count == 1;

        foreach (BufferedCityObject bufferedCityObject in cityObjects.OrderBy(
                     static bufferedCityObject => bufferedCityObject.CityObject.SlotKey,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CityObjectBakeCandidate? candidate = await CreateCandidateAsync(bufferedCityObject, cancellationToken);
            if (candidate is null)
            {
                continue;
            }

            if (candidate.AtlasEntries.Count == 0)
            {
                passThroughCandidates.Add(candidate);
                continue;
            }

            if (currentAtlasBatch.Count == 0)
            {
                if (CanFitSingleCandidate(candidate))
                {
                    currentAtlasBatch.Add(candidate);
                }
                else
                {
                    await EmitFallbackCandidateAsync(candidate, onBakedCityObject, cancellationToken);
                }

                continue;
            }

            if (CanAppendToAtlasBatch(currentAtlasBatch, candidate))
            {
                currentAtlasBatch.Add(candidate);
                continue;
            }

            await EmitAtlasBatchAsync(
                sourceUnitKey,
                currentAtlasBatch,
                batchIndex++,
                preservePrimaryIdentity && passThroughCandidates.Count == 0,
                onBakedCityObject,
                cancellationToken);
            currentAtlasBatch.Clear();

            if (CanFitSingleCandidate(candidate))
            {
                currentAtlasBatch.Add(candidate);
            }
            else
            {
                await EmitFallbackCandidateAsync(candidate, onBakedCityObject, cancellationToken);
            }
        }

        if (currentAtlasBatch.Count > 0)
        {
            await EmitAtlasBatchAsync(
                sourceUnitKey,
                currentAtlasBatch,
                batchIndex++,
                preservePrimaryIdentity && passThroughCandidates.Count == 0,
                onBakedCityObject,
                cancellationToken);
            currentAtlasBatch.Clear();
        }

        if (passThroughCandidates.Count == 1)
        {
            CityObjectBakeCandidate passThroughCandidate = passThroughCandidates[0];
            BakedOutputCityObjectCount++;
            await onBakedCityObject(passThroughCandidate.CityObject, cancellationToken);
            DisposeCandidateImages(passThroughCandidate);
        }
        else if (passThroughCandidates.Count > 1)
        {
            try
            {
                ResoniteConstructionCityObject mergedPassThroughCityObject = await BakeBatchAsync(
                    sourceUnitKey,
                    passThroughCandidates,
                    batchIndex,
                    preservePrimaryIdentity: false,
                    cancellationToken);
                BakedOutputCityObjectCount++;
                await onBakedCityObject(mergedPassThroughCityObject, cancellationToken);
            }
            finally
            {
                DisposeCandidateImages(passThroughCandidates);
            }
        }
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

    private async Task<CityObjectBakeCandidate?> CreateCandidateAsync(
        BufferedCityObject bufferedCityObject,
        CancellationToken cancellationToken)
    {
        ResoniteConstructionCityObject cityObject = bufferedCityObject.CityObject;
        Lod2AtlasCityObjectBakePolicy policy = bufferedCityObject.Policy;
        if (!TryCreateMaterialBySubmeshIndex(cityObject, out Dictionary<int, ResoniteMaterialBinding>? materialBySubmeshIndex))
        {
            throw new InvalidOperationException(
                $"LOD2 atlas bake city object '{cityObject.DisplayName}' contained duplicate material assignments for a submesh.");
        }

        List<AtlasBatchEntry> atlasEntries = [];
        List<PreservedSubmeshEntry> preservedEntries = [];
        foreach (ResoniteMeshSubmesh submesh in cityObject.Mesh.Submeshes.OrderBy(static candidate => candidate.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!materialBySubmeshIndex.TryGetValue(submesh.Index, out ResoniteMaterialBinding? material))
            {
                throw new InvalidOperationException(
                    $"LOD2 atlas bake city object '{cityObject.DisplayName}' left submesh index {submesh.Index} without a material assignment.");
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
            DisposeCandidateImages(new CityObjectBakeCandidate(cityObject, atlasEntries, preservedEntries));
            return null;
        }

        if (atlasEntries.Count == 0 && preservedEntries.Count == 0)
        {
            throw new InvalidOperationException(
                $"LOD2 atlas bake city object '{cityObject.DisplayName}' produced no atlas or preserved submesh candidate.");
        }

        return new CityObjectBakeCandidate(cityObject, atlasEntries, preservedEntries);
    }

    private async Task<MaterialAtlasTile> CreateAtlasTileAsync(
        ResoniteMaterialBinding material,
        UvBounds uvBounds,
        CancellationToken cancellationToken)
    {
        if (material.TexturePayload is null)
        {
            return CreateSolidColorTile(material.BaseColor);
        }

        using Image<Rgba32> sourceImage = await textureImageLoader.LoadAsync(
            ResoniteTextureImportFactory.CreateRawFromPayload(material.TexturePayload),
            cancellationToken);

        int maxTileWidth = EffectiveMaxAtlasTextureEdge;
        int maxTileHeight = EffectiveMaxAtlasTextureEdge;
        int targetWidth = Math.Max(1, Math.Min(maxTileWidth, (int)Math.Ceiling(sourceImage.Width * uvBounds.Width)));
        int targetHeight = Math.Max(1, Math.Min(maxTileHeight, (int)Math.Ceiling(sourceImage.Height * uvBounds.Height)));
        using Image<Rgba32> bakedImage = BakeUsedUvRegion(sourceImage, uvBounds, targetWidth, targetHeight);

        ApplyBaseColor(bakedImage, material.BaseColor);
        return new MaterialAtlasTile(material.TexturePayload.Identity ?? material.MaterialKey, bakedImage.Clone());
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
            && material.TexturePayload is not null
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

        if (material.TexturePayload is null)
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
        SourceUnitBatchKey sourceUnitKey,
        IReadOnlyList<CityObjectBakeCandidate> candidates,
        int batchIndex,
        bool preservePrimaryIdentity,
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
        string slotKey = preservePrimaryIdentity
            ? firstCityObject.SlotKey
            : CreateBatchSlotKey(sourceUnitKey, batchIndex);
        string displayName = preservePrimaryIdentity
            ? firstCityObject.DisplayName
            : CreateBatchDisplayName(sourceUnitKey, batchIndex);
        string? sourceObjectKey = preservePrimaryIdentity
            ? firstCityObject.SourceObjectKey
            : CreateBatchSourceObjectKey(sourceUnitKey, batchIndex);
        string? sourceUnitKeyValue = sourceUnitKey.SourceUnitKey ?? firstCityObject.SourceUnitKey;
        string? sourceFileRelativePath = sourceUnitKey.SourceFileRelativePath ?? firstCityObject.SourceFileRelativePath;

        ResoniteFloat3 bakeOrigin = ComputeBakeOrigin(candidates);
        List<ResoniteMeshVertex> vertices = [];
        List<ResoniteMeshSubmesh> submeshes = [];
        List<ResoniteMaterialBinding> materials = [];

        if (layout is not null)
        {
            string textureIdentity = CreateAtlasTextureIdentity(sourceUnitKey, batchIndex);
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
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    TexturePayload: ResoniteTextureImportFactory.CreatePayloadFromImage(atlasImage!, identity: textureIdentity)));
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

        if (submeshes.Count == 0 || materials.Count == 0)
        {
            throw new InvalidOperationException(
                $"LOD2 atlas bake batch '{sourceUnitKey.PackageName}:{sourceUnitKey.ActualMeshCode}:LOD{sourceUnitKey.LodLevel}' produced no materialized submesh.");
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
            SourceUnitKey: sourceUnitKeyValue,
            SourceFileRelativePath: sourceFileRelativePath);
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
        int atlasMaxSize = EffectiveMaxAtlasSize;
        List<AtlasPlacement> placements = [];
        List<Rect> freeRectangles = [new Rect(0, 0, atlasMaxSize, atlasMaxSize)];
        int usedWidth = 0;
        int usedHeight = 0;

        foreach (AtlasBatchEntry entry in entries)
        {
            int paddedWidth = entry.Tile.Image.Width + (tilePaddingPixels * 2);
            int paddedHeight = entry.Tile.Image.Height + (tilePaddingPixels * 2);
            if (paddedWidth > atlasMaxSize || paddedHeight > atlasMaxSize)
            {
                layout = null;
                return false;
            }

            if (!TryChooseFreeRectangle(freeRectangles, paddedWidth, paddedHeight, out Rect selectedRect))
            {
                layout = null;
                return false;
            }

            Rect outerRect = new(selectedRect.X, selectedRect.Y, paddedWidth, paddedHeight);
            Rect innerRect = new(selectedRect.X + tilePaddingPixels, selectedRect.Y + tilePaddingPixels, entry.Tile.Image.Width, entry.Tile.Image.Height);
            placements.Add(new AtlasPlacement(entry, outerRect, innerRect));
            SplitFreeRectangles(freeRectangles, outerRect);
            PruneFreeRectangles(freeRectangles);
            usedWidth = Math.Max(usedWidth, outerRect.X + outerRect.Width);
            usedHeight = Math.Max(usedHeight, outerRect.Y + outerRect.Height);
        }

        layout = new AtlasLayout(
            Math.Max(1, usedWidth),
            Math.Max(1, usedHeight),
            placements);
        return true;
    }

    private static bool TryChooseFreeRectangle(
        IReadOnlyList<Rect> freeRectangles,
        int requiredWidth,
        int requiredHeight,
        out Rect selectedRect)
    {
        selectedRect = default;
        bool found = false;
        int bestAreaFit = int.MaxValue;
        int bestShortSideFit = int.MaxValue;

        foreach (Rect freeRect in freeRectangles)
        {
            if (requiredWidth > freeRect.Width || requiredHeight > freeRect.Height)
            {
                continue;
            }

            int areaFit = (freeRect.Width * freeRect.Height) - (requiredWidth * requiredHeight);
            int shortSideFit = Math.Min(freeRect.Width - requiredWidth, freeRect.Height - requiredHeight);
            if (areaFit < bestAreaFit
                || (areaFit == bestAreaFit && shortSideFit < bestShortSideFit)
                || (areaFit == bestAreaFit && shortSideFit == bestShortSideFit
                    && (freeRect.Y < selectedRect.Y
                        || (freeRect.Y == selectedRect.Y && freeRect.X < selectedRect.X))))
            {
                selectedRect = freeRect;
                bestAreaFit = areaFit;
                bestShortSideFit = shortSideFit;
                found = true;
            }
        }

        return found;
    }

    private static void SplitFreeRectangles(List<Rect> freeRectangles, Rect usedRect)
    {
        for (int index = freeRectangles.Count - 1; index >= 0; index--)
        {
            Rect freeRect = freeRectangles[index];
            if (!Intersects(freeRect, usedRect))
            {
                continue;
            }

            freeRectangles.RemoveAt(index);

            if (usedRect.X > freeRect.X)
            {
                freeRectangles.Add(new Rect(
                    freeRect.X,
                    freeRect.Y,
                    usedRect.X - freeRect.X,
                    freeRect.Height));
            }

            if (usedRect.X + usedRect.Width < freeRect.X + freeRect.Width)
            {
                freeRectangles.Add(new Rect(
                    usedRect.X + usedRect.Width,
                    freeRect.Y,
                    (freeRect.X + freeRect.Width) - (usedRect.X + usedRect.Width),
                    freeRect.Height));
            }

            if (usedRect.Y > freeRect.Y)
            {
                freeRectangles.Add(new Rect(
                    freeRect.X,
                    freeRect.Y,
                    freeRect.Width,
                    usedRect.Y - freeRect.Y));
            }

            if (usedRect.Y + usedRect.Height < freeRect.Y + freeRect.Height)
            {
                freeRectangles.Add(new Rect(
                    freeRect.X,
                    usedRect.Y + usedRect.Height,
                    freeRect.Width,
                    (freeRect.Y + freeRect.Height) - (usedRect.Y + usedRect.Height)));
            }
        }
    }

    private static void PruneFreeRectangles(List<Rect> freeRectangles)
    {
        for (int leftIndex = freeRectangles.Count - 1; leftIndex >= 0; leftIndex--)
        {
            Rect left = freeRectangles[leftIndex];
            for (int rightIndex = freeRectangles.Count - 1; rightIndex >= 0; rightIndex--)
            {
                if (leftIndex == rightIndex)
                {
                    continue;
                }

                Rect right = freeRectangles[rightIndex];
                if (Contains(right, left))
                {
                    freeRectangles.RemoveAt(leftIndex);
                    break;
                }
            }
        }
    }

    private static bool Intersects(Rect left, Rect right)
    {
        return left.X < right.X + right.Width
            && left.X + left.Width > right.X
            && left.Y < right.Y + right.Height
            && left.Y + left.Height > right.Y;
    }

    private static bool Contains(Rect outer, Rect inner)
    {
        return inner.X >= outer.X
            && inner.Y >= outer.Y
            && inner.X + inner.Width <= outer.X + outer.Width
            && inner.Y + inner.Height <= outer.Y + outer.Height;
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

    private static SourceUnitBatchKey CreateSourceUnitKey(
        ResoniteConstructionCityObject cityObject,
        Lod2AtlasCityObjectBakePolicy policy)
    {
        string context = policy.Name;
        string sourceUnitKey = cityObject.SourceUnitKey ?? string.Empty;
        string sourceFileRelativePath = cityObject.SourceFileRelativePath ?? string.Empty;
        string sourceUnitIdentity = string.Create(
            CultureInfo.InvariantCulture,
            $"{cityObject.ActualMeshCode}|{cityObject.PackageName}|{cityObject.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none"}|{sourceUnitKey}|{sourceFileRelativePath}");

        return new SourceUnitBatchKey(
            cityObject.ActualMeshCode,
            cityObject.PackageName,
            cityObject.LodLevel,
            sourceUnitIdentity,
            context,
            SourceUnitKey: cityObject.SourceUnitKey,
            SourceFileRelativePath: cityObject.SourceFileRelativePath);
    }

    private static string CreateBatchSlotKey(SourceUnitBatchKey sourceUnitKey, int batchIndex)
    {
        string lodToken = sourceUnitKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        string policyToken = CreatePolicyContextToken(sourceUnitKey.PolicyContext);
        string sourceToken = CreateSourceUnitToken(sourceUnitKey.BatchScopeIdentity);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"atlasbake_{sourceUnitKey.PackageName}_{sourceUnitKey.ActualMeshCode}_{policyToken}_{sourceToken}_{lodToken}_{batchIndex:D4}");
    }

    private static string CreateBatchDisplayName(SourceUnitBatchKey sourceUnitKey, int batchIndex)
    {
        string lodToken = sourceUnitKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"AtlasBake {sourceUnitKey.PackageName} LOD{lodToken} {CreatePolicyContextToken(sourceUnitKey.PolicyContext)}-{CreateSourceUnitToken(sourceUnitKey.BatchScopeIdentity)} #{batchIndex + 1}");
    }

    private static string CreateBatchSourceObjectKey(SourceUnitBatchKey sourceUnitKey, int batchIndex)
    {
        string lodToken = sourceUnitKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"atlasbake:{sourceUnitKey.ActualMeshCode}:{sourceUnitKey.PackageName}:{CreatePolicyContextToken(sourceUnitKey.PolicyContext)}:{CreateSourceUnitToken(sourceUnitKey.BatchScopeIdentity)}:{lodToken}:{batchIndex:D4}");
    }

    private static string CreateAtlasTextureIdentity(SourceUnitBatchKey sourceUnitKey, int batchIndex)
    {
        string lodToken = sourceUnitKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"atlas-batch-{sourceUnitKey.ActualMeshCode}-{CreatePolicyContextToken(sourceUnitKey.PolicyContext)}-{sourceUnitKey.PackageName}-{lodToken}-{CreateSourceUnitToken(sourceUnitKey.BatchScopeIdentity)}-{batchIndex:D4}");
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
            normalizedMaterial.TexturePayload?.Identity,
            normalizedMaterial.TextureSourceKind,
            normalizedMaterial.TerrainOverlay,
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
            && material.TexturePayload is null
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

        int bundledVariantIndex = 0;
        string canonicalTexturePath = BundledDefaultMaterialFamilies.GetVariant(material.Family, bundledVariantIndex);
        return material with
        {
            MaterialKey = $"common-{material.Family}-variant:{bundledVariantIndex}",
            TextureSourceKind = ResoniteTextureSourceKind.Bundled,
            TextureScale = BundledDefaultMaterialProfiles.GetTilesPerMeter(canonicalTexturePath),
            TextureOffset = null,
            BundledVariantIndex = bundledVariantIndex,
        };
    }

    private async Task EmitAtlasBatchAsync(
        SourceUnitBatchKey sourceUnitKey,
        IReadOnlyList<CityObjectBakeCandidate> batchCandidates,
        int batchIndex,
        bool preservePrimaryIdentity,
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken)
    {
        try
        {
            ResoniteConstructionCityObject bakedCityObject = await BakeBatchAsync(
                sourceUnitKey,
                batchCandidates,
                batchIndex,
                preservePrimaryIdentity,
                cancellationToken);
            BakedOutputCityObjectCount++;
            await onBakedCityObject(bakedCityObject, cancellationToken);
        }
        finally
        {
            DisposeCandidateImages(batchCandidates);
        }
    }

    private async Task EmitFallbackCandidateAsync(
        CityObjectBakeCandidate fallbackCandidate,
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken)
    {
        try
        {
            BakedOutputCityObjectCount++;
            await onBakedCityObject(fallbackCandidate.CityObject, cancellationToken);
        }
        finally
        {
            DisposeCandidateImages(fallbackCandidate);
        }
    }

    private bool CanFitSingleCandidate(CityObjectBakeCandidate candidate)
    {
        return candidate.AtlasEntries.Count == 0 || TryCreateAtlasLayout(candidate.AtlasEntries, out _);
    }

    private bool CanAppendToAtlasBatch(
        IReadOnlyList<CityObjectBakeCandidate> batchCandidates,
        CityObjectBakeCandidate candidate)
    {
        List<AtlasBatchEntry> candidateEntries = [.. batchCandidates.SelectMany(static current => current.AtlasEntries), .. candidate.AtlasEntries];
        return TryCreateAtlasLayout(candidateEntries, out _);
    }

    private static void DisposeCandidateImages(CityObjectBakeCandidate candidate)
    {
        DisposeCandidateImages([candidate]);
    }

    private static void DisposeCandidateImages(IReadOnlyList<CityObjectBakeCandidate> candidates)
    {
        foreach (Image<Rgba32> tileImage in candidates
                     .SelectMany(static candidate => candidate.AtlasEntries)
                     .Select(static entry => entry.Tile.Image)
                     .Distinct())
        {
            tileImage.Dispose();
        }
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

    private readonly record struct SourceUnitBatchKey(
        string ActualMeshCode,
        string PackageName,
        int? LodLevel,
        string BatchScopeIdentity,
        string PolicyContext,
        string? SourceUnitKey,
        string? SourceFileRelativePath);

    private readonly record struct PreservedMaterialGroupingKey(
        string MaterialKey,
        ResoniteColor BaseColor,
        ResoniteMaterialType MaterialType,
        string? TextureIdentity,
        ResoniteTextureSourceKind TextureSourceKind,
        TerrainTextureOverlay? TerrainOverlay,
        ResoniteMaterialProjection Projection,
        ResoniteMaterialDepthOffset? DepthOffset,
        ResoniteFloat2? TextureScale,
        string? Family,
        ResoniteFloat2? TextureOffset,
        ResoniteMaterialAssetScope AssetScope);

    private sealed class SourceUnitKeyComparer : IComparer<SourceUnitBatchKey>
    {
        internal static readonly SourceUnitKeyComparer Instance = new();

        public int Compare(SourceUnitBatchKey x, SourceUnitBatchKey y)
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

            return string.CompareOrdinal(x.BatchScopeIdentity, y.BatchScopeIdentity);
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
                && string.Equals(x.TextureIdentity, y.TextureIdentity, StringComparison.Ordinal)
                && x.TextureSourceKind == y.TextureSourceKind
                && EqualityComparer<TerrainTextureOverlay?>.Default.Equals(x.TerrainOverlay, y.TerrainOverlay)
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
            hash.Add(obj.TextureIdentity, StringComparer.Ordinal);
            hash.Add(obj.TextureSourceKind);
            hash.Add(obj.TerrainOverlay);
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
