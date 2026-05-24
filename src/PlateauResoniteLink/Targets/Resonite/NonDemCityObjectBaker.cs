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

    private readonly Dictionary<NonDemBakeSourceFileKey, List<BufferedCityObject>> bufferedCityObjectsBySourceFile = [];
    private readonly Dictionary<NonDemBakeSourceFileKey, int> nextBatchIndexBySourceFile = [];
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
        NonDemBakeSourceFileKey sourceFileKey = NonDemBakeSourceScope.Create(cityObject, policy);
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

        NonDemBakeSourceFileKey[] orderedSourceFileKeys = bufferedCityObjectsBySourceFile.Keys
            .OrderBy(static key => key, NonDemBakeSourceScope.OrderComparer)
            .ToArray();
        foreach (NonDemBakeSourceFileKey sourceFileKey in orderedSourceFileKeys)
        {
            await EmitSourceFileAsync(sourceFileKey, onBakedCityObject, cancellationToken);
        }
    }

    private async Task EmitSourceFileAsync(
        NonDemBakeSourceFileKey sourceFileKey,
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
        NonDemBakeSourceFileKey sourceFileKey,
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
                && NonDemBakeMaterialClassifier.CanBufferCityObjectMaterials(cityObject, policy))
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
        if (!NonDemBakeMaterialClassifier.TryCreateMaterialBySubmeshIndex(cityObject, out _))
        {
            throw new InvalidOperationException(
                $"Non-DEM bake city object '{cityObject.DisplayName}' contained duplicate material assignments for a submesh.");
        }

        ResoniteConstructionCityObject normalizedCityObject = ResoniteDynamicMaterialUvNormalizer.Normalize(cityObject);
        if (!NonDemBakeMaterialClassifier.TryCreateMaterialBySubmeshIndex(normalizedCityObject, out Dictionary<int, ResoniteMaterialBinding>? materialBySubmeshIndex))
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

            NonDemMaterialBakeCategory category = NonDemBakeMaterialClassifier.ClassifyMaterial(material);
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

        TextureUvRect uvBounds = NonDemBakeGeometry.ComputeUvBounds(cityObject.Mesh.Vertices, submesh);
        using Image<Rgba32> sourceImage = await textureImageLoader.LoadAsync(
            ResoniteTextureImportFactory.CreateRawFromPayload(material.TexturePayload),
            cancellationToken);
        Rgba32 detectedBackgroundColor = NonDemBakeTextureProcessing.DetectRepresentativeBackgroundColor(sourceImage);
        using Image<Rgba32> preparedSourceImage = sourceImage.Clone();
        NonDemBakeTextureProcessing.FillTransparentRgb(preparedSourceImage, detectedBackgroundColor);
        if (NonDemBakeTextureProcessing.TryGetUniformPixelColor(preparedSourceImage, out Rgba32 uniformDatasetColor))
        {
            return new AtlasOrPreservedEntry(
                AtlasEntry: null,
                PreservedEntry: new PreservedSubmeshEntry(
                    cityObject,
                    submesh,
                    CreateVertexColorMaterial(material, submesh.Index),
                    NonDemBakeTextureProcessing.ToColor(NonDemBakeTextureProcessing.MultiplyPixel(
                        uniformDatasetColor,
                        NonDemBakeTextureProcessing.ToPixel(material.BaseColor)))));
        }

        int maxTileWidth = EffectiveMaxAtlasTextureEdge;
        int maxTileHeight = EffectiveMaxAtlasTextureEdge;
        int targetWidth = Math.Max(1, Math.Min(maxTileWidth, (int)Math.Ceiling(sourceImage.Width * uvBounds.Width)));
        int targetHeight = Math.Max(1, Math.Min(maxTileHeight, (int)Math.Ceiling(sourceImage.Height * uvBounds.Height)));
        using Image<Rgba32> bakedImage = NonDemBakeTextureProcessing.BakeUsedUvRegion(preparedSourceImage, uvBounds, targetWidth, targetHeight);

        NonDemBakeTextureProcessing.ApplyBaseColor(bakedImage, material.BaseColor);
        return new AtlasOrPreservedEntry(
            AtlasEntry: new AtlasBatchEntry(
                cityObject,
                submesh,
                material,
                new MaterialAtlasTile(
                    bakedImage.Clone(),
                    NonDemBakeTextureProcessing.MultiplyPixel(
                        detectedBackgroundColor,
                        NonDemBakeTextureProcessing.ToPixel(material.BaseColor))),
                uvBounds),
            PreservedEntry: null);
    }

    private async Task<ResoniteConstructionCityObject> BakeBatchAsync(
        NonDemBakeSourceFileKey sourceFileKey,
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
                NonDemBakeTextureProcessing.DrawAtlasTile(
                    atlasImage!,
                    atlasCoverage!,
                    layout.Width,
                    tilePaddingPixels,
                    placement,
                    static entry => entry.Tile.Image);
            }

            NonDemBakeTextureProcessing.FillUncoveredAtlasPixels(
                atlasImage!,
                atlasCoverage!,
                ComputeAtlasBackgroundColor(layout.Placements));
        }

        ResoniteConstructionCityObject firstCityObject = candidates[0].CityObject;
        string slotKey = preservePrimaryIdentity
            ? firstCityObject.SlotKey
            : NonDemBakeSourceScope.CreateBatchSlotKey(sourceFileKey, batchIndex);
        string displayName = preservePrimaryIdentity
            ? firstCityObject.DisplayName
            : NonDemBakeSourceScope.CreateBatchDisplayName(sourceFileKey, batchIndex, slotKey);
        string? sourceFileRelativePath = string.IsNullOrWhiteSpace(firstCityObject.SourceFileRelativePath)
            ? null
            : sourceFileKey.SourceFileRelativePath;

        ResoniteFloat3 bakeOrigin = NonDemBakeGeometry.ComputeBakeOrigin(
            candidates.Select(static candidate => candidate.CityObject));
        List<ResoniteMeshVertex> vertices = [];
        List<ResoniteMeshSubmesh> submeshes = [];
        List<ResoniteMaterialBinding> materials = [];

        if (layout is not null)
        {
            string textureIdentity = NonDemBakeSourceScope.CreateAtlasTextureIdentity(sourceFileKey, batchIndex);
            List<int> atlasTriangleIndices = [];
            foreach (NonDemAtlasPlacement<AtlasBatchEntry> placement in layout.Placements.OrderBy(static candidate => candidate.Entry.CityObject.SlotKey, StringComparer.Ordinal).ThenBy(static candidate => candidate.Entry.Submesh.Index))
            {
                cancellationToken.ThrowIfCancellationRequested();
                NonDemBakeGeometry.AppendAtlasGeometry(
                    vertices,
                    atlasTriangleIndices,
                    bakeOrigin,
                    placement.Entry.CityObject,
                    placement.Entry.Submesh,
                    placement.Entry.UvBounds,
                    placement.InnerRect,
                    layout.Width,
                    layout.Height);
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

        foreach (IGrouping<NonDemPreservedMaterialGroupingKey, OrderedPreservedSubmeshEntry> preservedGroup in candidates
                     .SelectMany(static candidate => candidate.PreservedEntries)
                     .Select(static (entry, order) => new OrderedPreservedSubmeshEntry(entry, order))
                     .GroupBy(
                         static entry => NonDemPreservedMaterialGrouping.CreateKey(entry.Entry.Material),
                         NonDemPreservedMaterialGrouping.KeyComparer)
                     .OrderBy(static group => group.Min(static entry => entry.Order)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<int> preservedTriangleIndices = [];
            foreach (PreservedSubmeshEntry preservedEntry in preservedGroup
                         .Select(static entry => entry.Entry)
                         .OrderBy(static entry => entry.CityObject.SlotKey, StringComparer.Ordinal)
                         .ThenBy(static entry => entry.Submesh.Index))
            {
                NonDemBakeGeometry.AppendOriginalGeometry(
                    vertices,
                    preservedTriangleIndices,
                    bakeOrigin,
                    preservedEntry.CityObject,
                    preservedEntry.Submesh,
                    preservedEntry.VertexColorOverride);
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

    private bool TryCreateAtlasLayout(
        IReadOnlyList<AtlasBatchEntry> entries,
        out NonDemAtlasLayout<AtlasBatchEntry>? layout)
    {
        return NonDemAtlasLayoutPlanner.TryCreateLayout(
            entries,
            EffectiveMaxAtlasSize,
            tilePaddingPixels,
            static entry => new NonDemAtlasTileSize(entry.Tile.Image.Width, entry.Tile.Image.Height),
            out layout);
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

    private static Rgba32 ComputeAtlasBackgroundColor(IReadOnlyList<NonDemAtlasPlacement<AtlasBatchEntry>> placements)
    {
        return NonDemBakeTextureProcessing.ComputeWeightedBackgroundColor(
            placements.Select(static placement => (
                placement.Entry.Tile.BackgroundColor,
                (long)placement.Entry.Tile.Image.Width * placement.Entry.Tile.Image.Height)));
    }

    private async Task EmitAtlasBatchAsync(
        NonDemBakeSourceFileKey sourceFileKey,
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

}
