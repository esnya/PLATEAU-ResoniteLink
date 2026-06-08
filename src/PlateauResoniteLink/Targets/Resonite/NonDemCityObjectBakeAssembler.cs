using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class NonDemCityObjectBakeAssembler(
    NonDemAtlasLayoutFactory atlasLayoutFactory,
    NonDemAtlasImageRenderer atlasImageRenderer)
{
    private readonly NonDemAtlasLayoutFactory atlasLayoutFactory = atlasLayoutFactory
        ?? throw new ArgumentNullException(nameof(atlasLayoutFactory));
    private readonly NonDemAtlasImageRenderer atlasImageRenderer = atlasImageRenderer
        ?? throw new ArgumentNullException(nameof(atlasImageRenderer));

    public Task<ResoniteConstructionCityObject> BakeBatchAsync(
        NonDemSourceFileBatchKey sourceFileKey,
        IReadOnlyList<NonDemCityObjectBakeCandidate> candidates,
        int batchIndex,
        bool preservePrimaryIdentity,
        CancellationToken cancellationToken)
    {
        List<NonDemAtlasBatchEntry> entries = candidates.SelectMany(static candidate => candidate.AtlasEntries).ToList();
        NonDemAtlasLayout<NonDemAtlasBatchEntry>? layout = null;
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
            atlasImageRenderer.Draw(atlasImage!, layout.Placements);
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

        NonDemBakedGeometry geometry = NonDemBakedGeometryComposer.Compose(
            sourceFileKey,
            candidates,
            batchIndex,
            layout,
            atlasImage,
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
}
