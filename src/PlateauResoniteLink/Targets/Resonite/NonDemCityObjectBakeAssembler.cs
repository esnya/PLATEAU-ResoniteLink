using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface INonDemCityObjectBakeAssembler
{
    Task<ResoniteConstructionCityObject> BakeBatchAsync(
        NonDemSourceFileBatchKey sourceFileKey,
        IReadOnlyList<NonDemCityObjectBakeCandidate> candidates,
        int batchIndex,
        bool preservePrimaryIdentity,
        CancellationToken cancellationToken);
}

internal sealed class NonDemCityObjectBakeAssembler(
    INonDemAtlasLayoutFactory atlasLayoutFactory,
    INonDemAtlasImageRenderer atlasImageRenderer,
    INonDemBakedGeometryComposer geometryComposer) : INonDemCityObjectBakeAssembler
{
    private readonly INonDemAtlasLayoutFactory atlasLayoutFactory = atlasLayoutFactory
        ?? throw new ArgumentNullException(nameof(atlasLayoutFactory));
    private readonly INonDemAtlasImageRenderer atlasImageRenderer = atlasImageRenderer
        ?? throw new ArgumentNullException(nameof(atlasImageRenderer));
    private readonly INonDemBakedGeometryComposer geometryComposer = geometryComposer
        ?? throw new ArgumentNullException(nameof(geometryComposer));

    public Task<ResoniteConstructionCityObject> BakeBatchAsync(
        NonDemSourceFileBatchKey sourceFileKey,
        IReadOnlyList<NonDemCityObjectBakeCandidate> candidates,
        int batchIndex,
        bool preservePrimaryIdentity,
        CancellationToken cancellationToken)
    {
        List<NonDemAtlasBatchEntry> entries = candidates.SelectMany(static candidate => candidate.AtlasEntries).ToList();
        using NonDemRenderedAtlas? atlas = RenderAtlas(entries, cancellationToken);

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

        NonDemBakedGeometry geometry = geometryComposer.Compose(
            sourceFileKey,
            candidates,
            batchIndex,
            atlas,
            cancellationToken);

        ResoniteConstructionCityObject bakedCityObject = new(
            SlotKey: slotKey,
            DisplayName: displayName,
            PackageName: firstCityObject.PackageName,
            ActualMeshCode: firstCityObject.ActualMeshCode,
            LodLevel: firstCityObject.LodLevel,
            Transform: new ResoniteTransform(geometry.Origin),
            Mesh: geometry.Mesh,
            Materials: geometry.Materials,
            CollisionEnabled: candidates.Any(static candidate => candidate.CityObject.CollisionEnabled),
            SourceFileRelativePath: sourceFileRelativePath);
        return Task.FromResult(bakedCityObject);
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "NonDemRenderedAtlas takes ownership of the rendered image and disposes it after geometry composition.")]
    private NonDemRenderedAtlas? RenderAtlas(
        List<NonDemAtlasBatchEntry> entries,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        if (!atlasLayoutFactory.TryCreate(entries, out NonDemAtlasLayout<NonDemAtlasBatchEntry>? layout) || layout is null)
        {
            throw new InvalidOperationException("Failed to create non-DEM atlas layout.");
        }

        Image<Rgba32> atlasImage = new(layout.Width, layout.Height, new Rgba32(0, 0, 0, 0));
        cancellationToken.ThrowIfCancellationRequested();
        atlasImageRenderer.Draw(atlasImage, layout.Placements);
        return new NonDemRenderedAtlas(layout, atlasImage);
    }
}
