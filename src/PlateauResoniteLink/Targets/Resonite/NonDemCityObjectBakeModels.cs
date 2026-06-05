using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record NonDemMaterialAtlasTile(Image<Rgba32> Image, Rgba32 BackgroundColor);

internal sealed record NonDemAtlasOrPreservedEntry(
    NonDemAtlasBatchEntry? AtlasEntry,
    NonDemPreservedSubmeshEntry? PreservedEntry);

internal readonly record struct NonDemBufferedCityObject(
    ResoniteConstructionCityObject CityObject,
    NonDemCityObjectBakePolicy Policy);

internal sealed record NonDemAtlasBatchEntry(
    ResoniteConstructionCityObject CityObject,
    ResoniteMeshSubmesh Submesh,
    ResoniteMaterialBinding Material,
    NonDemMaterialAtlasTile Tile,
    TextureUvRect UvBounds);

internal sealed record NonDemPreservedSubmeshEntry(
    ResoniteConstructionCityObject CityObject,
    ResoniteMeshSubmesh Submesh,
    ResoniteMaterialBinding Material,
    ResoniteColor? VertexColorOverride = null);

internal sealed record NonDemOrderedPreservedSubmeshEntry(
    NonDemPreservedSubmeshEntry Entry,
    int Order);

internal sealed record NonDemCityObjectBakeCandidate(
    ResoniteConstructionCityObject CityObject,
    IReadOnlyList<NonDemAtlasBatchEntry> AtlasEntries,
    IReadOnlyList<NonDemPreservedSubmeshEntry> PreservedEntries);
