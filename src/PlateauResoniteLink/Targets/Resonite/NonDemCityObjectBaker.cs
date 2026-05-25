using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly Dictionary<NonDemSourceFileBatchKey, List<NonDemBufferedCityObject>> bufferedCityObjectsBySourceFile = [];
    private readonly Dictionary<NonDemSourceFileBatchKey, int> nextBatchIndexBySourceFile = [];
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
        NonDemSourceFileBatchKey sourceFileKey = NonDemSourceFileBatching.CreateKey(cityObject, policy);
        List<ResoniteConstructionCityObject> readyCityObjects = [];
        NonDemBufferedCityObject bufferedCityObject = new(cityObject, policy);
        if (!bufferedCityObjectsBySourceFile.TryGetValue(sourceFileKey, out List<NonDemBufferedCityObject>? bufferedCityObjects))
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

        NonDemSourceFileBatchKey[] orderedSourceFileKeys = bufferedCityObjectsBySourceFile.Keys
            .OrderBy(static key => key, NonDemSourceFileBatching.KeyComparer)
            .ToArray();
        foreach (NonDemSourceFileBatchKey sourceFileKey in orderedSourceFileKeys)
        {
            await EmitSourceFileAsync(sourceFileKey, onBakedCityObject, cancellationToken);
        }
    }

    private async Task EmitSourceFileAsync(
        NonDemSourceFileBatchKey sourceFileKey,
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken)
    {
        if (!bufferedCityObjectsBySourceFile.Remove(sourceFileKey, out List<NonDemBufferedCityObject>? cityObjects))
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
        NonDemSourceFileBatchKey sourceFileKey,
        List<NonDemBufferedCityObject> cityObjects,
        int batchStartIndex,
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken)
    {
        List<NonDemCityObjectBakeCandidate> passThroughCandidates = [];
        List<NonDemCityObjectBakeCandidate> currentAtlasBatch = [];
        int batchIndex = batchStartIndex;
        bool preservePrimaryIdentity = cityObjects.Count == 1;

        foreach (NonDemBufferedCityObject bufferedCityObject in cityObjects.OrderBy(
                     static bufferedCityObject => bufferedCityObject.CityObject.SlotKey,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            NonDemCityObjectBakeCandidate? candidate = await CreateCandidateAsync(bufferedCityObject, cancellationToken);
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
            NonDemCityObjectBakeCandidate passThroughCandidate = passThroughCandidates[0];
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

    private async Task<NonDemCityObjectBakeCandidate?> CreateCandidateAsync(
        NonDemBufferedCityObject bufferedCityObject,
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

        List<NonDemAtlasBatchEntry> atlasEntries = [];
        List<NonDemPreservedSubmeshEntry> preservedEntries = [];
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
                    NonDemAtlasOrPreservedEntry bakeEntry = await CreateAtlasOrPreservedEntryAsync(
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
                    preservedEntries.Add(new NonDemPreservedSubmeshEntry(normalizedCityObject, normalizedSubmesh, normalizedMaterial));
                    break;
            }
        }

        if (policy.RequireAtlasCandidateMaterial && !hadAtlasCandidateMaterial)
        {
            DisposeCandidateImages(new NonDemCityObjectBakeCandidate(normalizedCityObject, atlasEntries, preservedEntries));
            return null;
        }

        if (atlasEntries.Count == 0 && preservedEntries.Count == 0)
        {
            throw new InvalidOperationException(
                $"Non-DEM bake city object '{cityObject.DisplayName}' produced no atlas or preserved submesh candidate.");
        }

        return new NonDemCityObjectBakeCandidate(normalizedCityObject, atlasEntries, preservedEntries);
    }

    private async Task<NonDemAtlasOrPreservedEntry> CreateAtlasOrPreservedEntryAsync(
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
        Rgba32 detectedBackgroundColor = NonDemTextureImageProcessing.DetectRepresentativeBackgroundColor(sourceImage);
        using Image<Rgba32> preparedSourceImage = sourceImage.Clone();
        NonDemTextureImageProcessing.FillTransparentRgb(preparedSourceImage, detectedBackgroundColor);
        if (NonDemTextureImageProcessing.TryGetUniformPixelColor(preparedSourceImage, out Rgba32 uniformDatasetColor))
        {
            Rgba32 tintedUniformColor = NonDemTextureImageProcessing.MultiplyPixel(
                uniformDatasetColor,
                NonDemTextureImageProcessing.ToPixel(material.BaseColor));
            return new NonDemAtlasOrPreservedEntry(
                AtlasEntry: null,
                PreservedEntry: new NonDemPreservedSubmeshEntry(
                    cityObject,
                    submesh,
                    CreateVertexColorMaterial(material, submesh.Index),
                    NonDemTextureImageProcessing.ToColor(tintedUniformColor)));
        }

        int maxTileWidth = EffectiveMaxAtlasTextureEdge;
        int maxTileHeight = EffectiveMaxAtlasTextureEdge;
        int targetWidth = Math.Max(1, Math.Min(maxTileWidth, (int)Math.Ceiling(sourceImage.Width * uvBounds.Width)));
        int targetHeight = Math.Max(1, Math.Min(maxTileHeight, (int)Math.Ceiling(sourceImage.Height * uvBounds.Height)));
        using Image<Rgba32> bakedImage = NonDemTextureImageProcessing.BakeUsedUvRegion(
            preparedSourceImage,
            uvBounds,
            targetWidth,
            targetHeight);

        NonDemTextureImageProcessing.ApplyBaseColor(bakedImage, material.BaseColor);
        return new NonDemAtlasOrPreservedEntry(
            AtlasEntry: new NonDemAtlasBatchEntry(
                cityObject,
                submesh,
                material,
                new NonDemMaterialAtlasTile(
                    bakedImage.Clone(),
                    NonDemTextureImageProcessing.MultiplyPixel(
                        detectedBackgroundColor,
                        NonDemTextureImageProcessing.ToPixel(material.BaseColor))),
                uvBounds),
            PreservedEntry: null);
    }

    private async Task<ResoniteConstructionCityObject> BakeBatchAsync(
        NonDemSourceFileBatchKey sourceFileKey,
        IReadOnlyList<NonDemCityObjectBakeCandidate> candidates,
        int batchIndex,
        bool preservePrimaryIdentity,
        CancellationToken cancellationToken)
    {
        List<NonDemAtlasBatchEntry> entries = candidates.SelectMany(static candidate => candidate.AtlasEntries).ToList();
        NonDemAtlasLayout<NonDemAtlasBatchEntry>? layout = null;
        NonDemAtlasLayoutFactory atlasLayoutFactory = CreateAtlasLayoutFactory();
        if (entries.Count > 0
            && (!atlasLayoutFactory.TryCreate(entries, out layout) || layout is null))
        {
            throw new InvalidOperationException("Failed to create non-DEM atlas layout.");
        }

        using Image<Rgba32>? atlasImage = layout is null
            ? null
            : new Image<Rgba32>(layout.Width, layout.Height, new Rgba32(0, 0, 0, 0));
        if (layout is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            new NonDemAtlasImageRenderer(tilePaddingPixels).Draw(atlasImage!, layout.Placements);
        }

        ResoniteConstructionCityObject firstCityObject = candidates[0].CityObject;
        string slotKey = preservePrimaryIdentity
            ? firstCityObject.SlotKey
            : NonDemSourceFileBatching.CreateBatchSlotKey(sourceFileKey, batchIndex);
        string displayName = preservePrimaryIdentity
            ? firstCityObject.DisplayName
            : NonDemSourceFileBatching.CreateBatchDisplayName(sourceFileKey, batchIndex, slotKey);
        string? sourceFileRelativePath = string.IsNullOrWhiteSpace(firstCityObject.SourceFileRelativePath)
            ? null
            : sourceFileKey.SourceFileRelativePath;

        ResoniteFloat3 bakeOrigin = ComputeBakeOrigin(candidates);
        List<ResoniteMeshVertex> vertices = [];
        List<ResoniteMeshSubmesh> submeshes = [];
        List<ResoniteMaterialBinding> materials = [];

        if (layout is not null)
        {
            string textureIdentity = NonDemSourceFileBatching.CreateAtlasTextureIdentity(sourceFileKey, batchIndex);
            List<int> atlasTriangleIndices = [];
            foreach (NonDemAtlasPlacement<NonDemAtlasBatchEntry> placement in layout.Placements.OrderBy(static candidate => candidate.Entry.CityObject.SlotKey, StringComparer.Ordinal).ThenBy(static candidate => candidate.Entry.Submesh.Index))
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

        foreach (IGrouping<NonDemPreservedMaterialGroupingKey, NonDemOrderedPreservedSubmeshEntry> preservedGroup in candidates
                     .SelectMany(static candidate => candidate.PreservedEntries)
                     .Select(static (entry, order) => new NonDemOrderedPreservedSubmeshEntry(entry, order))
                     .GroupBy(static entry => NonDemPreservedMaterialGrouping.CreateKey(entry.Entry.Material), NonDemPreservedMaterialGrouping.KeyComparer)
                     .OrderBy(static group => group.Min(static entry => entry.Order)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<int> preservedTriangleIndices = [];
            foreach (NonDemPreservedSubmeshEntry preservedEntry in preservedGroup
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
            ResoniteMaterialBinding preservedMaterial = NonDemPreservedMaterialGrouping.NormalizeMaterial(preservedGroup.First().Entry.Material) with
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
        NonDemAtlasPlacement<NonDemAtlasBatchEntry> placement,
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
        NonDemPreservedSubmeshEntry preservedEntry)
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

    private static ResoniteFloat3 ComputeBakeOrigin(IReadOnlyList<NonDemCityObjectBakeCandidate> candidates)
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

    private NonDemAtlasLayoutFactory CreateAtlasLayoutFactory()
    {
        return new NonDemAtlasLayoutFactory(EffectiveMaxAtlasSize, tilePaddingPixels);
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

    private async Task EmitAtlasBatchAsync(
        NonDemSourceFileBatchKey sourceFileKey,
        IReadOnlyList<NonDemCityObjectBakeCandidate> batchCandidates,
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
        NonDemCityObjectBakeCandidate fallbackCandidate,
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

    private bool CanFitSingleCandidate(NonDemCityObjectBakeCandidate candidate)
    {
        return candidate.AtlasEntries.Count == 0 || CreateAtlasLayoutFactory().CanFit(candidate.AtlasEntries);
    }

    private bool CanAppendToAtlasBatch(
        IReadOnlyList<NonDemCityObjectBakeCandidate> batchCandidates,
        NonDemCityObjectBakeCandidate candidate)
    {
        List<NonDemAtlasBatchEntry> candidateEntries = [.. batchCandidates.SelectMany(static current => current.AtlasEntries), .. candidate.AtlasEntries];
        return CreateAtlasLayoutFactory().CanFit(candidateEntries);
    }

    private static void DisposeCandidateImages(NonDemCityObjectBakeCandidate candidate)
    {
        DisposeCandidateImages([candidate]);
    }

    private static void DisposeCandidateImages(IReadOnlyList<NonDemCityObjectBakeCandidate> candidates)
    {
        foreach (Image<Rgba32> tileImage in candidates
                     .SelectMany(static candidate => candidate.AtlasEntries)
                     .Select(static entry => entry.Tile.Image)
                     .Distinct())
        {
            tileImage.Dispose();
        }
    }

    private static bool RequiresBakeEmission(NonDemCityObjectBakeCandidate candidate)
    {
        return candidate.PreservedEntries.Any(static entry => entry.VertexColorOverride is not null);
    }

}
