using System.Collections.Generic;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record QueuedCityObject(
    ResoniteConstructionCityObject CityObject,
    Task<ResoniteSharedSlotIndex.ObjectSlotHierarchy> ObjectHierarchyTask,
    AsyncWeightedGate.Lease MemoryLease);

internal abstract record PreparedConstructionGeometry;

internal sealed record PreparedTriangleMeshGeometry(
    ImportMeshRawData MeshImport)
    : PreparedConstructionGeometry;

internal sealed record PreparedTerrainGridGeometry(
    ResoniteTerrainGridGeometry Geometry,
    ResoniteRawHdrTextureImport HeightTextureImport)
    : PreparedConstructionGeometry;

internal sealed record PreparedDynamicTerrainGeometry(
    PreparedTriangleMeshGeometry StaticMesh,
    PreparedTerrainGridGeometry GridMesh)
    : PreparedConstructionGeometry;

internal sealed record PreparedCityObject(
    ResoniteConstructionCityObject CityObject,
    PreparedConstructionGeometry Geometry,
    IReadOnlyList<PreparedTextureReference> Textures);

internal sealed record PreparedTextureReference(
    ResoniteTexturePayload? TexturePayload,
    ResoniteTextureSourceKind TextureSourceKind,
    ResoniteTextureImport TextureImport,
    string? TerrainMeshCode = null,
    TerrainTextureOverlay? TerrainOverlay = null,
    GeneratedTerrainTexture? GeneratedTerrainTexture = null);
