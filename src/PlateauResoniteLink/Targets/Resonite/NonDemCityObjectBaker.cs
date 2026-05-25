using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class NonDemCityObjectBaker(
    ResoniteTextureImageLoader textureImageLoader,
    IReadOnlyList<NonDemCityObjectBakePolicy> bakePolicies,
    int maxAtlasSize = 4096,
    int tilePaddingPixels = 2,
    ResoniteImportBudgetProfile? resourceBudget = null) : IResoniteBufferedCityObjectBaker
{
    internal const int DefaultMaxAtlasSize = 4096;
    internal const int DefaultTilePaddingPixels = 2;
    private const byte BackgroundDetectionAlphaThreshold = 16;

    private readonly Dictionary<SourceFileBatchKey, List<BufferedCityObject>> bufferedCityObjectsBySourceFile = [];
    private readonly Dictionary<SourceFileBatchKey, int> nextBatchIndexBySourceFile = [];
    private readonly IReadOnlyList<NonDemCityObjectBakePolicy> bakePolicies = bakePolicies
        ?? throw new ArgumentNullException(nameof(bakePolicies));

    public string Name => "AtlasBake";

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

    public ValueTask<BufferedCityObjectBufferResult> TryBufferAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        cancellationToken.ThrowIfCancellationRequested();

        NonDemCityObjectBakePolicy? policy = ResolvePolicy(cityObject);
        if (policy is null)
        {
            return ValueTask.FromResult(new BufferedCityObjectBufferResult(Buffered: false, []));
        }

        cityObject = ResoniteDynamicMaterialUvNormalizer.Normalize(cityObject);
        SourceFileBatchKey sourceFileKey = CreateSourceFileKey(cityObject, policy);
        List<ResoniteConstructionCityObject> readyCityObjects = [];
        BufferedCityObject bufferedCityObject = new(cityObject, policy);
        if (!bufferedCityObjectsBySourceFile.TryGetValue(sourceFileKey, out List<BufferedCityObject>? bufferedCityObjects))
        {
            bufferedCityObjects = [];
            bufferedCityObjectsBySourceFile.Add(sourceFileKey, bufferedCityObjects);
        }

        bufferedCityObjects.Add(bufferedCityObject);
        BakedInputCityObjectCount++;
        return ValueTask.FromResult(new BufferedCityObjectBufferResult(Buffered: true, readyCityObjects));
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
        if (bufferedCityObjectsBySourceFile.Count == 0)
        {
            return;
        }

        SourceFileBatchKey[] orderedSourceFileKeys = bufferedCityObjectsBySourceFile.Keys
            .OrderBy(static key => key, SourceFileBatchKeyComparer.Instance)
            .ToArray();
        foreach (SourceFileBatchKey sourceFileKey in orderedSourceFileKeys)
        {
            await EmitSourceFileAsync(sourceFileKey, onBakedCityObject, cancellationToken);
        }
    }

    private async Task EmitSourceFileAsync(
        SourceFileBatchKey sourceFileKey,
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken)
    {
        if (!bufferedCityObjectsBySourceFile.Remove(sourceFileKey, out List<BufferedCityObject>? cityObjects))
        {
            return;
        }

        int emittedCount = 0;
        int batchStartIndex = nextBatchIndexBySourceFile.GetValueOrDefault(sourceFileKey);

        await BakeSourceFileAsync(
            sourceFileKey,
            cityObjects,
            batchStartIndex,
            (bakedCityObject, callbackCancellationToken) =>
            {
                emittedCount++;
                return onBakedCityObject(bakedCityObject, callbackCancellationToken);
            },
            cancellationToken);

        nextBatchIndexBySourceFile[sourceFileKey] = batchStartIndex + emittedCount;
        cityObjects.Clear();
    }

    private async Task BakeSourceFileAsync(
        SourceFileBatchKey sourceFileKey,
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

            if (candidate.AtlasEntries.Count == 0 && !RequiresBakeEmission(candidate))
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
                sourceFileKey,
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
                sourceFileKey,
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
                    sourceFileKey,
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

    private NonDemCityObjectBakePolicy? ResolvePolicy(ResoniteConstructionCityObject cityObject)
    {
        foreach (NonDemCityObjectBakePolicy policy in bakePolicies)
        {
            if (policy.CanBuffer(cityObject)
                && NonDemCityObjectBakeMaterialClassifier.CanBufferCityObjectMaterials(cityObject, policy))
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
        NonDemCityObjectBakePolicy policy = bufferedCityObject.Policy;
        if (!NonDemCityObjectBakeMaterialClassifier.TryCreateMaterialBySubmeshIndex(cityObject, out _))
        {
            throw new InvalidOperationException(
                $"Non-DEM bake city object '{cityObject.DisplayName}' contained duplicate material assignments for a submesh.");
        }

        ResoniteConstructionCityObject normalizedCityObject = ResoniteDynamicMaterialUvNormalizer.Normalize(cityObject);
        if (!NonDemCityObjectBakeMaterialClassifier.TryCreateMaterialBySubmeshIndex(normalizedCityObject, out Dictionary<int, ResoniteMaterialBinding>? materialBySubmeshIndex))
        {
            throw new InvalidOperationException(
                $"Non-DEM bake city object '{cityObject.DisplayName}' contained duplicate material assignments for a submesh.");
        }

        List<AtlasBatchEntry> atlasEntries = [];
        List<PreservedSubmeshEntry> preservedEntries = [];
        bool hadAtlasCandidateMaterial = false;
        foreach (ResoniteMeshSubmesh submesh in normalizedCityObject.Mesh.Submeshes.OrderBy(static candidate => candidate.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!materialBySubmeshIndex.TryGetValue(submesh.Index, out ResoniteMaterialBinding? material))
            {
                throw new InvalidOperationException(
                    $"Non-DEM bake city object '{cityObject.DisplayName}' left submesh index {submesh.Index} without a material assignment.");
            }

            NonDemMaterialBakeCategory category = NonDemCityObjectBakeMaterialClassifier.Classify(material);
            switch (category)
            {
                case NonDemMaterialBakeCategory.AtlasCandidate:
                    hadAtlasCandidateMaterial = true;
                    AtlasOrPreservedEntry bakeEntry = await CreateAtlasOrPreservedEntryAsync(
                        normalizedCityObject,
                        submesh,
                        material,
                        cancellationToken);
                    if (bakeEntry.AtlasEntry is not null)
                    {
                        atlasEntries.Add(bakeEntry.AtlasEntry);
                    }

                    if (bakeEntry.PreservedEntry is not null)
                    {
                        preservedEntries.Add(bakeEntry.PreservedEntry);
                    }

                    break;
                case NonDemMaterialBakeCategory.PreservedCommonMaterial when policy.PreserveCommonMaterials:
                case NonDemMaterialBakeCategory.PreservedTextureless when policy.PreserveTexturelessMaterials:
                case NonDemMaterialBakeCategory.PreservedVertexColor when policy.PreserveVertexColorMaterials:
                case NonDemMaterialBakeCategory.PreservedOther:
                    ResoniteMeshSubmesh normalizedSubmesh = normalizedCityObject.Mesh.Submeshes.Single(candidate => candidate.Index == submesh.Index);
                    ResoniteMaterialBinding normalizedMaterial = normalizedCityObject.Materials.Single(candidate => candidate.SubmeshIndices.Contains(submesh.Index));
                    preservedEntries.Add(new PreservedSubmeshEntry(normalizedCityObject, normalizedSubmesh, normalizedMaterial));
                    break;
            }
        }

        if (policy.RequireAtlasCandidateMaterial && !hadAtlasCandidateMaterial)
        {
            DisposeCandidateImages(new CityObjectBakeCandidate(normalizedCityObject, atlasEntries, preservedEntries));
            return null;
        }

        if (atlasEntries.Count == 0 && preservedEntries.Count == 0)
        {
            throw new InvalidOperationException(
                $"Non-DEM bake city object '{cityObject.DisplayName}' produced no atlas or preserved submesh candidate.");
        }

        return new CityObjectBakeCandidate(normalizedCityObject, atlasEntries, preservedEntries);
    }

    private async Task<AtlasOrPreservedEntry> CreateAtlasOrPreservedEntryAsync(
        ResoniteConstructionCityObject cityObject,
        ResoniteMeshSubmesh submesh,
        ResoniteMaterialBinding material,
        CancellationToken cancellationToken)
    {
        if (material.TexturePayload is null)
        {
            throw new InvalidOperationException("Non-DEM bake candidate material must have a texture payload.");
        }

        TextureUvRect uvBounds = ComputeUvBounds(cityObject.Mesh.Vertices, submesh, material);
        using Image<Rgba32> sourceImage = await textureImageLoader.LoadAsync(
            ResoniteTextureImportFactory.CreateRawFromPayload(material.TexturePayload),
            cancellationToken);
        Rgba32 detectedBackgroundColor = DetectRepresentativeBackgroundColor(sourceImage);
        using Image<Rgba32> preparedSourceImage = sourceImage.Clone();
        FillTransparentRgb(preparedSourceImage, detectedBackgroundColor);
        if (TryGetUniformPixelColor(preparedSourceImage, out Rgba32 uniformDatasetColor))
        {
            return new AtlasOrPreservedEntry(
                AtlasEntry: null,
                PreservedEntry: new PreservedSubmeshEntry(
                    cityObject,
                    submesh,
                    CreateVertexColorMaterial(material, submesh.Index),
                    ToColor(MultiplyPixel(uniformDatasetColor, ToPixel(material.BaseColor)))));
        }

        int maxTileWidth = EffectiveMaxAtlasTextureEdge;
        int maxTileHeight = EffectiveMaxAtlasTextureEdge;
        int targetWidth = Math.Max(1, Math.Min(maxTileWidth, (int)Math.Ceiling(sourceImage.Width * uvBounds.Width)));
        int targetHeight = Math.Max(1, Math.Min(maxTileHeight, (int)Math.Ceiling(sourceImage.Height * uvBounds.Height)));
        using Image<Rgba32> bakedImage = BakeUsedUvRegion(preparedSourceImage, uvBounds, targetWidth, targetHeight);

        ApplyBaseColor(bakedImage, material.BaseColor);
        return new AtlasOrPreservedEntry(
            AtlasEntry: new AtlasBatchEntry(
                cityObject,
                submesh,
                material,
                new MaterialAtlasTile(
                    bakedImage.Clone(),
                    MultiplyPixel(detectedBackgroundColor, ToPixel(material.BaseColor))),
                uvBounds),
            PreservedEntry: null);
    }

    private AtlasBatchPlan CreateAtlasCandidateBatches(
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
        SourceFileBatchKey sourceFileKey,
        IReadOnlyList<CityObjectBakeCandidate> candidates,
        int batchIndex,
        bool preservePrimaryIdentity,
        CancellationToken cancellationToken)
    {
        List<AtlasBatchEntry> entries = candidates.SelectMany(static candidate => candidate.AtlasEntries).ToList();
        NonDemAtlasLayout<AtlasBatchEntry>? layout = null;
        if (entries.Count > 0
            && (!TryCreateAtlasLayout(entries, out layout) || layout is null))
        {
            throw new InvalidOperationException("Failed to create non-DEM atlas layout.");
        }

        using Image<Rgba32>? atlasImage = layout is null
            ? null
            : new Image<Rgba32>(layout.Width, layout.Height, new Rgba32(0, 0, 0, 0));
        bool[]? atlasCoverage = layout is null
            ? null
            : new bool[layout.Width * layout.Height];
        if (layout is not null)
        {
            foreach (NonDemAtlasPlacement<AtlasBatchEntry> placement in layout.Placements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DrawAtlasTile(atlasImage!, atlasCoverage!, layout.Width, placement);
            }

            FillUncoveredAtlasPixels(
                atlasImage!,
                atlasCoverage!,
                ComputeAtlasBackgroundColor(layout.Placements));
        }

        ResoniteConstructionCityObject firstCityObject = candidates[0].CityObject;
        string slotKey = preservePrimaryIdentity
            ? firstCityObject.SlotKey
            : CreateBatchSlotKey(sourceFileKey, batchIndex);
        string displayName = preservePrimaryIdentity
            ? firstCityObject.DisplayName
            : CreateBatchDisplayName(sourceFileKey, batchIndex, slotKey);
        string? sourceFileRelativePath = string.IsNullOrWhiteSpace(firstCityObject.SourceFileRelativePath)
            ? null
            : sourceFileKey.SourceFileRelativePath;

        ResoniteFloat3 bakeOrigin = ComputeBakeOrigin(candidates);
        List<ResoniteMeshVertex> vertices = [];
        List<ResoniteMeshSubmesh> submeshes = [];
        List<ResoniteMaterialBinding> materials = [];

        if (layout is not null)
        {
            string textureIdentity = CreateAtlasTextureIdentity(sourceFileKey, batchIndex);
            List<int> atlasTriangleIndices = [];
            foreach (NonDemAtlasPlacement<AtlasBatchEntry> placement in layout.Placements.OrderBy(static candidate => candidate.Entry.CityObject.SlotKey, StringComparer.Ordinal).ThenBy(static candidate => candidate.Entry.Submesh.Index))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendPlacementGeometry(vertices, atlasTriangleIndices, bakeOrigin, placement, layout.Width, layout.Height);
            }

            submeshes.Add(new ResoniteMeshSubmesh(0, atlasTriangleIndices));
            materials.Add(
                new ResoniteMaterialBinding(
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0],
                    TexturePayload: ResoniteTextureImportFactory.CreatePayloadFromImage(atlasImage!, identity: textureIdentity),
                    CommonMaterial: CommonMaterialCatalog.Create().Generic.Uv));
        }

        foreach (IGrouping<PreservedMaterialGroupingKey, OrderedPreservedSubmeshEntry> preservedGroup in candidates
                     .SelectMany(static candidate => candidate.PreservedEntries)
                     .Select(static (entry, order) => new OrderedPreservedSubmeshEntry(entry, order))
                     .GroupBy(static entry => CreatePreservedMaterialGroupingKey(entry.Entry.Material), PreservedMaterialGroupingKeyComparer.Instance)
                     .OrderBy(static group => group.Min(static entry => entry.Order)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<int> preservedTriangleIndices = [];
            foreach (PreservedSubmeshEntry preservedEntry in preservedGroup
                         .Select(static entry => entry.Entry)
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
            ResoniteMaterialBinding preservedMaterial = NormalizePreservedMaterial(preservedGroup.First().Entry.Material) with
            {
                SubmeshIndices = [submeshIndex],
            };
            submeshes.Add(new ResoniteMeshSubmesh(submeshIndex, preservedTriangleIndices));
            materials.Add(preservedMaterial);
        }

        if (submeshes.Count == 0 || materials.Count == 0)
        {
            throw new InvalidOperationException(
                $"Non-DEM bake batch '{sourceFileKey.PackageName}:{sourceFileKey.ActualMeshCode}:LOD{sourceFileKey.LodLevel}' produced no materialized submesh.");
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
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static void AppendPlacementGeometry(
        List<ResoniteMeshVertex> vertices,
        List<int> triangleIndices,
        ResoniteFloat3 bakeOrigin,
        NonDemAtlasPlacement<AtlasBatchEntry> placement,
        int atlasWidth,
        int atlasHeight)
    {
        IReadOnlyList<ResoniteMeshVertex> sourceVertices = placement.Entry.CityObject.Mesh.Vertices;
        ResoniteFloat3 cityObjectOffset = Subtract(placement.Entry.CityObject.Transform.Position, bakeOrigin);
        NonDemAtlasRect innerRect = placement.InnerRect;

        foreach (int sourceIndex in placement.Entry.Submesh.TriangleVertexIndices)
        {
            ResoniteMeshVertex sourceVertex = sourceVertices[sourceIndex];
            ResoniteFloat2 sourceUv = sourceVertex.UV0;
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
                Color = preservedEntry.VertexColorOverride ?? sourceVertex.Color,
            });
            triangleIndices.Add(vertices.Count - 1);
        }
    }

    private static ResoniteFloat2 MapUvToAtlas(
        ResoniteFloat2 sourceUv,
        TextureUvRect uvBounds,
        NonDemAtlasRect atlasRect,
        double atlasWidth,
        double atlasHeight)
    {
        TextureUvRect atlasUvRect = TextureUvRect.FromTopLeftPixelRect(
            atlasRect.X,
            atlasRect.Y,
            atlasRect.Width,
            atlasRect.Height,
            (int)Math.Round(atlasWidth),
            (int)Math.Round(atlasHeight));
        ScalarPair remapped = TextureUvRect.RemapValue(
            new ScalarPair(sourceUv.X, sourceUv.Y),
            uvBounds,
            atlasUvRect);
        return new ResoniteFloat2(remapped.X, remapped.Y);
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

    private void DrawAtlasTile(Image<Rgba32> atlasImage, bool[] atlasCoverage, int atlasWidth, NonDemAtlasPlacement<AtlasBatchEntry> placement)
    {
        for (int y = 0; y < placement.Entry.Tile.Image.Height; y++)
        {
            for (int x = 0; x < placement.Entry.Tile.Image.Width; x++)
            {
                SetAtlasPixel(
                    atlasImage,
                    atlasCoverage,
                    atlasWidth,
                    placement.InnerRect.X + x,
                    placement.InnerRect.Y + y,
                    placement.Entry.Tile.Image[x, y]);
            }
        }

        for (int y = 0; y < placement.Entry.Tile.Image.Height; y++)
        {
            Rgba32 leftEdge = atlasImage[placement.InnerRect.X, placement.InnerRect.Y + y];
            Rgba32 rightEdge = atlasImage[placement.InnerRect.X + placement.InnerRect.Width - 1, placement.InnerRect.Y + y];
            for (int pad = 1; pad <= tilePaddingPixels; pad++)
            {
                SetAtlasPixel(
                    atlasImage,
                    atlasCoverage,
                    atlasWidth,
                    placement.InnerRect.X - pad,
                    placement.InnerRect.Y + y,
                    leftEdge);
                SetAtlasPixel(
                    atlasImage,
                    atlasCoverage,
                    atlasWidth,
                    placement.InnerRect.X + placement.InnerRect.Width - 1 + pad,
                    placement.InnerRect.Y + y,
                    rightEdge);
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
                SetAtlasPixel(
                    atlasImage,
                    atlasCoverage,
                    atlasWidth,
                    sampleX,
                    targetTopY,
                    atlasImage[sampleX, sourceTopY]);
                SetAtlasPixel(
                    atlasImage,
                    atlasCoverage,
                    atlasWidth,
                    sampleX,
                    targetBottomY,
                    atlasImage[sampleX, sourceBottomY]);
            }
        }
    }

    private bool TryCreateAtlasLayout(
        IReadOnlyList<AtlasBatchEntry> entries,
        out NonDemAtlasLayout<AtlasBatchEntry>? layout)
    {
        NonDemAtlasLayoutPacker packer = new(EffectiveMaxAtlasSize, tilePaddingPixels);
        return packer.TryCreate(
            entries,
            static entry => new NonDemAtlasTileSize(entry.Tile.Image.Width, entry.Tile.Image.Height),
            out layout);
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

    private static Rgba32 MultiplyPixel(Rgba32 left, Rgba32 right)
    {
        return new Rgba32(
            MultiplyChannel(left.R, right.R),
            MultiplyChannel(left.G, right.G),
            MultiplyChannel(left.B, right.B),
            MultiplyChannel(left.A, right.A));
    }

    private static Rgba32 ToPixel(ResoniteColor color)
    {
        return new Rgba32(
            (byte)Math.Round(Math.Clamp(color.R, 0.0, 1.0) * 255.0),
            (byte)Math.Round(Math.Clamp(color.G, 0.0, 1.0) * 255.0),
            (byte)Math.Round(Math.Clamp(color.B, 0.0, 1.0) * 255.0),
            (byte)Math.Round(Math.Clamp(color.A, 0.0, 1.0) * 255.0));
    }

    private static ResoniteColor ToColor(Rgba32 color)
    {
        return new ResoniteColor(
            color.R / 255.0,
            color.G / 255.0,
            color.B / 255.0,
            color.A / 255.0);
    }

    private static ResoniteMaterialBinding CreateVertexColorMaterial(ResoniteMaterialBinding material, int submeshIndex)
    {
        return material with
        {
            MaterialType = ResoniteMaterialType.VertexColor,
            BaseColor = new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            TexturePayload = null,
            TextureSourceKind = ResoniteTextureSourceKind.Bundled,
            TextureScale = null,
            TextureOffset = null,
            Family = null,
            TerrainOverlay = null,
            AssetScope = ResoniteMaterialAssetScope.Common,
            SubmeshIndices = [submeshIndex],
            CommonMaterial = material.DepthOffset is null
                ? CommonMaterialCatalog.Create().VertexColor.Uv
                : CommonMaterialCatalog.Create().VertexColor.TerrainAlignedUv,
        };
    }

    private static SourceFileBatchKey CreateSourceFileKey(
        ResoniteConstructionCityObject cityObject,
        NonDemCityObjectBakePolicy policy)
    {
        string context = policy.Name;
        string? sourceFileRelativePath = string.IsNullOrWhiteSpace(cityObject.SourceFileRelativePath) ? null : cityObject.SourceFileRelativePath;
        if (sourceFileRelativePath is null)
        {
            throw new InvalidOperationException(
                $"Non-DEM batch candidate '{cityObject.DisplayName}' did not provide source scope. "
                + "source-file-owned batching requires SourceFileRelativePath.");
        }

        return new SourceFileBatchKey(
            cityObject.ActualMeshCode,
            cityObject.PackageName.ToLowerInvariant(),
            cityObject.LodLevel,
            context,
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static string CreateBatchSlotKey(SourceFileBatchKey sourceFileKey, int batchIndex)
    {
        string lodToken = sourceFileKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"atlasbake-{Path.GetFileNameWithoutExtension(sourceFileKey.SourceFileRelativePath)}-{sourceFileKey.PackageName}-lod{lodToken}-{batchIndex + 1}");
    }

    private static string CreateBatchDisplayName(SourceFileBatchKey sourceFileKey, int batchIndex, string batchSlotKey)
    {
        string lodToken = sourceFileKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"AtlasBake {sourceFileKey.PackageName} LOD{lodToken} #{batchIndex + 1} [{batchSlotKey}]");
    }

    private static string CreateAtlasTextureIdentity(SourceFileBatchKey sourceFileKey, int batchIndex)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"atlastex-{sourceFileKey.SourceFileRelativePath}-{batchIndex + 1}");
    }

    private static ResoniteFloat3 Add(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    private static ResoniteFloat3 Subtract(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }

    private static TextureUvRect ComputeUvBounds(
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
            ResoniteFloat2 transformedUv = vertices[sourceIndex].UV0;
            minU = Math.Min(minU, transformedUv.X);
            minV = Math.Min(minV, transformedUv.Y);
            maxU = Math.Max(maxU, transformedUv.X);
            maxV = Math.Max(maxV, transformedUv.Y);
        }

        if (double.IsPositiveInfinity(minU) || double.IsPositiveInfinity(minV))
        {
            return TextureUvRect.Identity;
        }

        double width = Math.Max(1.0 / 1024.0, maxU - minU);
        double height = Math.Max(1.0 / 1024.0, maxV - minV);
        return new TextureUvRect(minU, minV, width, height);
    }

    private static Image<Rgba32> BakeUsedUvRegion(
        Image<Rgba32> sourceImage,
        TextureUvRect uvBounds,
        int targetWidth,
        int targetHeight)
    {
        Image<Rgba32> bakedImage = new(targetWidth, targetHeight);
        for (int y = 0; y < targetHeight; y++)
        {
            double normalizedV = 1.0 - ((y + 0.5) / targetHeight);
            for (int x = 0; x < targetWidth; x++)
            {
                double normalizedU = (x + 0.5) / targetWidth;
                ScalarPair sourceUv = uvBounds.DenormalizeValue(normalizedU, normalizedV);
                bakedImage[x, y] = SampleWrappedPixelBilinear(sourceImage, sourceUv.X, sourceUv.Y);
            }
        }

        return bakedImage;
    }

    private static Rgba32 DetectRepresentativeBackgroundColor(Image<Rgba32> image)
    {
        if (TryAverageBoundaryOpaquePixels(image, out Rgba32 boundaryAverage))
        {
            return boundaryAverage;
        }

        if (TryAverageOpaquePixels(image, out Rgba32 opaqueAverage))
        {
            return opaqueAverage;
        }

        if (TryAverageAllPixels(image, out Rgba32 allPixelAverage))
        {
            return new Rgba32(allPixelAverage.R, allPixelAverage.G, allPixelAverage.B, byte.MaxValue);
        }

        return new Rgba32(255, 255, 255, 255);
    }

    private static void FillTransparentRgb(Image<Rgba32> image, Rgba32 backgroundColor)
    {
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgba32 pixel = image[x, y];
                if (pixel.A == byte.MaxValue)
                {
                    continue;
                }

                double alpha = pixel.A / 255.0;
                image[x, y] = new Rgba32(
                    BlendBackgroundChannel(pixel.R, backgroundColor.R, alpha),
                    BlendBackgroundChannel(pixel.G, backgroundColor.G, alpha),
                    BlendBackgroundChannel(pixel.B, backgroundColor.B, alpha),
                    pixel.A);
            }
        }
    }

    private static bool TryGetUniformPixelColor(Image<Rgba32> image, out Rgba32 color)
    {
        color = default;
        if (image.Width <= 0 || image.Height <= 0)
        {
            return false;
        }

        Rgba32 firstPixel = image[0, 0];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (!image[x, y].Equals(firstPixel))
                {
                    return false;
                }
            }
        }

        color = firstPixel;
        return true;
    }

    private static bool TryAverageBoundaryOpaquePixels(Image<Rgba32> image, out Rgba32 color)
    {
        long sumR = 0;
        long sumG = 0;
        long sumB = 0;
        long count = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgba32 pixel = image[x, y];
                if (pixel.A <= BackgroundDetectionAlphaThreshold || !TouchesTransparentNeighbor(image, x, y))
                {
                    continue;
                }

                sumR += pixel.R;
                sumG += pixel.G;
                sumB += pixel.B;
                count++;
            }
        }

        color = count == 0
            ? default
            : new Rgba32(
                (byte)Math.Clamp(Math.Round(sumR / (double)count), 0.0, 255.0),
                (byte)Math.Clamp(Math.Round(sumG / (double)count), 0.0, 255.0),
                (byte)Math.Clamp(Math.Round(sumB / (double)count), 0.0, 255.0),
                byte.MaxValue);
        return count > 0;
    }

    private static bool TouchesTransparentNeighbor(Image<Rgba32> image, int x, int y)
    {
        return IsTransparentOrOutOfBounds(image, x - 1, y)
            || IsTransparentOrOutOfBounds(image, x + 1, y)
            || IsTransparentOrOutOfBounds(image, x, y - 1)
            || IsTransparentOrOutOfBounds(image, x, y + 1);
    }

    private static bool IsTransparentOrOutOfBounds(Image<Rgba32> image, int x, int y)
    {
        if (x < 0 || y < 0 || x >= image.Width || y >= image.Height)
        {
            return true;
        }

        return image[x, y].A <= BackgroundDetectionAlphaThreshold;
    }

    private static bool TryAverageOpaquePixels(Image<Rgba32> image, out Rgba32 color)
    {
        return TryAveragePixels(image, static pixel => pixel.A > BackgroundDetectionAlphaThreshold, out color);
    }

    private static bool TryAverageAllPixels(Image<Rgba32> image, out Rgba32 color)
    {
        return TryAveragePixels(image, static _ => true, out color);
    }

    private static bool TryAveragePixels(Image<Rgba32> image, Func<Rgba32, bool> predicate, out Rgba32 color)
    {
        long sumR = 0;
        long sumG = 0;
        long sumB = 0;
        long count = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgba32 pixel = image[x, y];
                if (!predicate(pixel))
                {
                    continue;
                }

                sumR += pixel.R;
                sumG += pixel.G;
                sumB += pixel.B;
                count++;
            }
        }

        color = count == 0
            ? default
            : new Rgba32(
                (byte)Math.Clamp(Math.Round(sumR / (double)count), 0.0, 255.0),
                (byte)Math.Clamp(Math.Round(sumG / (double)count), 0.0, 255.0),
                (byte)Math.Clamp(Math.Round(sumB / (double)count), 0.0, 255.0),
                byte.MaxValue);
        return count > 0;
    }

    private static byte BlendBackgroundChannel(byte foreground, byte background, double alpha)
    {
        double blended = (foreground * alpha) + (background * (1.0 - alpha));
        return (byte)Math.Clamp(Math.Round(blended), 0.0, 255.0);
    }

    private static Rgba32 ComputeAtlasBackgroundColor(IReadOnlyList<NonDemAtlasPlacement<AtlasBatchEntry>> placements)
    {
        long sumR = 0;
        long sumG = 0;
        long sumB = 0;
        long totalWeight = 0;
        foreach (NonDemAtlasPlacement<AtlasBatchEntry> placement in placements)
        {
            long weight = Math.Max(1, placement.Entry.Tile.Image.Width * placement.Entry.Tile.Image.Height);
            sumR += placement.Entry.Tile.BackgroundColor.R * weight;
            sumG += placement.Entry.Tile.BackgroundColor.G * weight;
            sumB += placement.Entry.Tile.BackgroundColor.B * weight;
            totalWeight += weight;
        }

        if (totalWeight == 0)
        {
            return new Rgba32(255, 255, 255, 255);
        }

        return new Rgba32(
            (byte)Math.Clamp(Math.Round(sumR / (double)totalWeight), 0.0, 255.0),
            (byte)Math.Clamp(Math.Round(sumG / (double)totalWeight), 0.0, 255.0),
            (byte)Math.Clamp(Math.Round(sumB / (double)totalWeight), 0.0, 255.0),
            byte.MaxValue);
    }

    private static void FillUncoveredAtlasPixels(Image<Rgba32> atlasImage, bool[] atlasCoverage, Rgba32 backgroundColor)
    {
        for (int y = 0; y < atlasImage.Height; y++)
        {
            for (int x = 0; x < atlasImage.Width; x++)
            {
                int offset = (y * atlasImage.Width) + x;
                if (atlasCoverage[offset])
                {
                    continue;
                }

                atlasImage[x, y] = backgroundColor;
            }
        }
    }

    private static void SetAtlasPixel(Image<Rgba32> atlasImage, bool[] atlasCoverage, int atlasWidth, int x, int y, Rgba32 pixel)
    {
        atlasImage[x, y] = pixel;
        atlasCoverage[(y * atlasWidth) + x] = true;
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

    private static PreservedMaterialGroupingKey CreatePreservedMaterialGroupingKey(ResoniteMaterialBinding material)
    {
        ResoniteMaterialBinding normalizedMaterial = NormalizePreservedMaterial(material);
        if (normalizedMaterial.CommonMaterial is not null)
        {
            return new PreservedMaterialGroupingKey(
                normalizedMaterial.CommonMaterial,
                new ResoniteColor(0.0, 0.0, 0.0, 0.0),
                default,
                normalizedMaterial.TexturePayload,
                normalizedMaterial.TextureSourceKind,
                normalizedMaterial.TerrainOverlay,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                normalizedMaterial.TerrainMeshCode);
        }

        return new PreservedMaterialGroupingKey(
            null,
            normalizedMaterial.BaseColor,
            normalizedMaterial.MaterialType,
            normalizedMaterial.TexturePayload,
            normalizedMaterial.TextureSourceKind,
            normalizedMaterial.TerrainOverlay,
            normalizedMaterial.Projection,
            normalizedMaterial.DepthOffset,
            normalizedMaterial.TextureScale,
            normalizedMaterial.TextureOffset,
            normalizedMaterial.AssetScope,
            normalizedMaterial.Family,
            normalizedMaterial.BundledVariantIndex,
            normalizedMaterial.TerrainMeshCode);
    }

    private static ResoniteMaterialBinding NormalizePreservedMaterial(ResoniteMaterialBinding material)
    {
        return ResoniteSceneMaterialConventions.NormalizeBatchGroupedMaterialBinding(material);
    }

    private async Task EmitAtlasBatchAsync(
        SourceFileBatchKey sourceFileKey,
        IReadOnlyList<CityObjectBakeCandidate> batchCandidates,
        int batchIndex,
        bool preservePrimaryIdentity,
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken)
    {
        try
        {
            ResoniteConstructionCityObject bakedCityObject = await BakeBatchAsync(
                sourceFileKey,
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

    private sealed record MaterialAtlasTile(Image<Rgba32> Image, Rgba32 BackgroundColor);

    private sealed record AtlasOrPreservedEntry(
        AtlasBatchEntry? AtlasEntry,
        PreservedSubmeshEntry? PreservedEntry);

    private readonly record struct BufferedCityObject(
        ResoniteConstructionCityObject CityObject,
        NonDemCityObjectBakePolicy Policy);

    private sealed record AtlasBatchEntry(
        ResoniteConstructionCityObject CityObject,
        ResoniteMeshSubmesh Submesh,
        ResoniteMaterialBinding Material,
        MaterialAtlasTile Tile,
        TextureUvRect UvBounds);

    private sealed record PreservedSubmeshEntry(
        ResoniteConstructionCityObject CityObject,
        ResoniteMeshSubmesh Submesh,
        ResoniteMaterialBinding Material,
        ResoniteColor? VertexColorOverride = null);

    private sealed record OrderedPreservedSubmeshEntry(
        PreservedSubmeshEntry Entry,
        int Order);

    private sealed record CityObjectBakeCandidate(
        ResoniteConstructionCityObject CityObject,
        IReadOnlyList<AtlasBatchEntry> AtlasEntries,
        IReadOnlyList<PreservedSubmeshEntry> PreservedEntries);

    private static bool RequiresBakeEmission(CityObjectBakeCandidate candidate)
    {
        return candidate.PreservedEntries.Any(static entry => entry.VertexColorOverride is not null);
    }

    private sealed record AtlasBatchPlan(
        IReadOnlyList<IReadOnlyList<CityObjectBakeCandidate>> Batches,
        IReadOnlyList<CityObjectBakeCandidate> FallbackCandidates);

    private readonly record struct SourceFileBatchKey(
        string ActualMeshCode,
        string PackageName,
        int? LodLevel,
        string PolicyContext,
        string SourceFileRelativePath);

    private readonly record struct PreservedMaterialGroupingKey(
        DefaultCommonMaterialMember? CommonMaterial,
        ResoniteColor BaseColor,
        ResoniteMaterialType MaterialType,
        ResoniteTexturePayload? TexturePayload,
        ResoniteTextureSourceKind TextureSourceKind,
        TerrainTextureOverlay? TerrainOverlay,
        ResoniteMaterialProjection Projection,
        ResoniteMaterialDepthOffset? DepthOffset,
        ResoniteFloat2? TextureScale,
        ResoniteFloat2? TextureOffset,
        ResoniteMaterialAssetScope AssetScope,
        string? Family,
        int? BundledVariantIndex,
        string? TerrainMeshCode);

    private sealed class SourceFileBatchKeyComparer : IComparer<SourceFileBatchKey>
    {
        internal static readonly SourceFileBatchKeyComparer Instance = new();

        public int Compare(SourceFileBatchKey x, SourceFileBatchKey y)
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

            compare = string.CompareOrdinal(x.SourceFileRelativePath, y.SourceFileRelativePath);
            if (compare != 0)
            {
                return compare;
            }

            return 0;
        }
    }

    private sealed class PreservedMaterialGroupingKeyComparer :
        IEqualityComparer<PreservedMaterialGroupingKey>
    {
        internal static readonly PreservedMaterialGroupingKeyComparer Instance = new();

        public bool Equals(PreservedMaterialGroupingKey x, PreservedMaterialGroupingKey y)
        {
            if (!EqualityComparer<DefaultCommonMaterialMember?>.Default.Equals(x.CommonMaterial, y.CommonMaterial))
            {
                return false;
            }

            if (x.CommonMaterial is not null)
            {
                return ReferenceEquals(x.TexturePayload, y.TexturePayload)
                    && x.TextureSourceKind == y.TextureSourceKind
                    && EqualityComparer<TerrainTextureOverlay?>.Default.Equals(x.TerrainOverlay, y.TerrainOverlay)
                    && string.Equals(x.TerrainMeshCode, y.TerrainMeshCode, StringComparison.Ordinal);
            }

            return x.BaseColor == y.BaseColor
                && x.MaterialType == y.MaterialType
                && ReferenceEquals(x.TexturePayload, y.TexturePayload)
                && x.TextureSourceKind == y.TextureSourceKind
                && EqualityComparer<TerrainTextureOverlay?>.Default.Equals(x.TerrainOverlay, y.TerrainOverlay)
                && x.Projection == y.Projection
                && EqualityComparer<ResoniteMaterialDepthOffset?>.Default.Equals(x.DepthOffset, y.DepthOffset)
                && EqualityComparer<ResoniteFloat2?>.Default.Equals(x.TextureScale, y.TextureScale)
                && EqualityComparer<ResoniteFloat2?>.Default.Equals(x.TextureOffset, y.TextureOffset)
                && x.AssetScope == y.AssetScope
                && string.Equals(x.Family, y.Family, StringComparison.Ordinal)
                && x.BundledVariantIndex == y.BundledVariantIndex
                && string.Equals(x.TerrainMeshCode, y.TerrainMeshCode, StringComparison.Ordinal);
        }

        public int GetHashCode(PreservedMaterialGroupingKey obj)
        {
            HashCode hash = new();
            hash.Add(obj.CommonMaterial);
            if (obj.CommonMaterial is not null)
            {
                if (obj.TexturePayload is not null)
                {
                    hash.Add(RuntimeHelpers.GetHashCode(obj.TexturePayload));
                }
                hash.Add(obj.TextureSourceKind);
                hash.Add(obj.TerrainOverlay);
                hash.Add(obj.TerrainMeshCode, StringComparer.Ordinal);
                return hash.ToHashCode();
            }

            hash.Add(obj.BaseColor);
            hash.Add(obj.MaterialType);
            if (obj.TexturePayload is not null)
            {
                hash.Add(RuntimeHelpers.GetHashCode(obj.TexturePayload));
            }
            hash.Add(obj.TextureSourceKind);
            hash.Add(obj.TerrainOverlay);
            hash.Add(obj.Projection);
            hash.Add(obj.DepthOffset);
            hash.Add(obj.TextureScale);
            hash.Add(obj.TextureOffset);
            hash.Add(obj.AssetScope);
            hash.Add(obj.Family, StringComparer.Ordinal);
            hash.Add(obj.BundledVariantIndex);
            hash.Add(obj.TerrainMeshCode, StringComparer.Ordinal);
            return hash.ToHashCode();
        }

    }
}
