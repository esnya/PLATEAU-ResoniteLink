using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

internal sealed class FixedCellCityObjectMeshBaker
{
    internal const double DefaultCellSizeMeters = 128.0;
    internal const int DefaultMaxCityObjectsPerBatch = 64;
    internal const int DefaultMaxVerticesPerBatch = 200_000;
    internal const int DefaultMaxBufferedCells = 32;

    private readonly double cellSizeMeters;
    private readonly int maxCityObjectsPerBatch;
    private readonly int maxVerticesPerBatch;
    private readonly int maxBufferedCells;
    private readonly Dictionary<CellKey, CellBuffer> buffers = [];
    private readonly Dictionary<CellKey, int> flushSequenceByCell = [];
    private long nextBufferSequence;

    public FixedCellCityObjectMeshBaker()
        : this(
            DefaultCellSizeMeters,
            DefaultMaxCityObjectsPerBatch,
            DefaultMaxVerticesPerBatch,
            DefaultMaxBufferedCells)
    {
    }

    internal FixedCellCityObjectMeshBaker(
        double cellSizeMeters,
        int maxCityObjectsPerBatch,
        int maxVerticesPerBatch,
        int maxBufferedCells = DefaultMaxBufferedCells)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cellSizeMeters, 0.0);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCityObjectsPerBatch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxVerticesPerBatch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBufferedCells);

        this.cellSizeMeters = cellSizeMeters;
        this.maxCityObjectsPerBatch = maxCityObjectsPerBatch;
        this.maxVerticesPerBatch = maxVerticesPerBatch;
        this.maxBufferedCells = maxBufferedCells;
    }

    public int BakedInputCityObjectCount { get; private set; }

    public int BakedOutputCityObjectCount { get; private set; }

    public bool TryBuffer(
        ResoniteConstructionCityObject cityObject,
        out ResoniteConstructionCityObject? bakedCityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        bakedCityObject = null;
        if (!CanBake(cityObject))
        {
            return false;
        }

        CellKey cellKey = CreateCellKey(cityObject);
        if (!buffers.TryGetValue(cellKey, out CellBuffer? buffer))
        {
            buffer = new CellBuffer();
            buffers.Add(cellKey, buffer);
        }

        buffer.CityObjects.Add(cityObject);
        buffer.VertexCount += cityObject.Mesh.Vertices.Count;
        buffer.LastTouchedSequence = nextBufferSequence++;
        BakedInputCityObjectCount++;

        if (buffer.CityObjects.Count < maxCityObjectsPerBatch
            && buffer.VertexCount < maxVerticesPerBatch)
        {
            _ = TryFlushOldestBufferExcept(cellKey, out bakedCityObject);
            return true;
        }

        buffers.Remove(cellKey);
        bakedCityObject = BakeCell(cellKey, buffer);
        return true;
    }

    public IReadOnlyList<ResoniteConstructionCityObject> FlushAll()
    {
        if (buffers.Count == 0)
        {
            return [];
        }

        List<ResoniteConstructionCityObject> bakedCityObjects = new(buffers.Count);
        foreach ((CellKey cellKey, CellBuffer buffer) in buffers
                     .OrderBy(static pair => pair.Key, CellKeyComparer.Instance))
        {
            bakedCityObjects.Add(BakeCell(cellKey, buffer));
        }

        buffers.Clear();
        return bakedCityObjects;
    }

    private static bool CanBake(ResoniteConstructionCityObject cityObject)
    {
        return string.Equals(cityObject.PackageName, "bldg", StringComparison.OrdinalIgnoreCase)
            && cityObject.LodLevel == 1
            && cityObject.Geometry is ResoniteTriangleMeshGeometry
            && cityObject.Transform.Rotation is null;
    }

    private CellKey CreateCellKey(ResoniteConstructionCityObject cityObject)
    {
        string sourceUnitKey = cityObject.SourceUnitKey ?? cityObject.SourceObjectKey ?? cityObject.SlotKey;
        if (ShouldBakeAsSingleEightDigitMesh(cityObject))
        {
            return new CellKey(
                cityObject.ActualMeshCode,
                cityObject.PackageName,
                cityObject.LodLevel,
                sourceUnitKey,
                CellX: 0,
                CellZ: 0);
        }

        int cellX = (int)Math.Floor(cityObject.Transform.Position.X / cellSizeMeters);
        int cellZ = (int)Math.Floor(cityObject.Transform.Position.Z / cellSizeMeters);
        return new CellKey(
            cityObject.ActualMeshCode,
            cityObject.PackageName,
            cityObject.LodLevel,
            sourceUnitKey,
            cellX,
            cellZ);
    }

    private static bool ShouldBakeAsSingleEightDigitMesh(ResoniteConstructionCityObject cityObject)
    {
        return CanBake(cityObject)
            && cityObject.ActualMeshCode.Length == 8;
    }

    private bool TryFlushOldestBufferExcept(
        CellKey protectedCellKey,
        out ResoniteConstructionCityObject? bakedCityObject)
    {
        bakedCityObject = null;
        if (buffers.Count <= maxBufferedCells)
        {
            return false;
        }

        KeyValuePair<CellKey, CellBuffer>? candidate = null;
        foreach ((CellKey cellKey, CellBuffer buffer) in buffers)
        {
            if (cellKey == protectedCellKey)
            {
                continue;
            }

            if (candidate is null
                || buffer.LastTouchedSequence < candidate.Value.Value.LastTouchedSequence
                || (buffer.LastTouchedSequence == candidate.Value.Value.LastTouchedSequence
                    && CellKeyComparer.Instance.Compare(cellKey, candidate.Value.Key) < 0))
            {
                candidate = new KeyValuePair<CellKey, CellBuffer>(cellKey, buffer);
            }
        }

        if (candidate is null)
        {
            return false;
        }

        buffers.Remove(candidate.Value.Key);
        bakedCityObject = BakeCell(candidate.Value.Key, candidate.Value.Value);
        return true;
    }

    private ResoniteConstructionCityObject BakeCell(CellKey cellKey, CellBuffer buffer)
    {
        int flushSequence = flushSequenceByCell.GetValueOrDefault(cellKey);
        flushSequenceByCell[cellKey] = flushSequence + 1;

        ResoniteFloat3 bakeOrigin = ComputeBakeOrigin(buffer);
        List<ResoniteMeshVertex> vertices = [];
        Dictionary<MaterialIdentity, List<int>> trianglesByMaterial = [];
        Dictionary<MaterialIdentity, ResoniteMaterialBinding> materialByIdentity = [];

        foreach (ResoniteConstructionCityObject cityObject in buffer.CityObjects
                     .OrderBy(static candidate => candidate.SlotKey, StringComparer.Ordinal))
        {
            int vertexOffset = vertices.Count;
            ResoniteFloat3 positionOffset = Subtract(cityObject.Transform.Position, bakeOrigin);
            foreach (ResoniteMeshVertex vertex in cityObject.Mesh.Vertices)
            {
                vertices.Add(vertex with
                {
                    Position = Add(vertex.Position, positionOffset),
                });
            }

            Dictionary<int, ResoniteMaterialBinding> materialBySubmeshIndex = cityObject.Materials
                .SelectMany(material => material.SubmeshIndices.Select(submeshIndex => (submeshIndex, material)))
                .ToDictionary(static pair => pair.submeshIndex, static pair => pair.material);

            foreach (ResoniteMeshSubmesh submesh in cityObject.Mesh.Submeshes.OrderBy(static submesh => submesh.Index))
            {
                if (!materialBySubmeshIndex.TryGetValue(submesh.Index, out ResoniteMaterialBinding? material))
                {
                    continue;
                }

                MaterialIdentity identity = MaterialIdentity.From(material);
                materialByIdentity.TryAdd(identity, material);
                if (!trianglesByMaterial.TryGetValue(identity, out List<int>? indices))
                {
                    indices = [];
                    trianglesByMaterial.Add(identity, indices);
                }

                foreach (int index in submesh.TriangleVertexIndices)
                {
                    indices.Add(index + vertexOffset);
                }
            }
        }

        List<ResoniteMaterialBinding> materials = [];
        List<ResoniteMeshSubmesh> submeshes = [];
        foreach (MaterialIdentity identity in trianglesByMaterial.Keys.OrderBy(static identity => identity, MaterialIdentityComparer.Instance))
        {
            List<int> indices = trianglesByMaterial[identity];
            if (indices.Count == 0)
            {
                continue;
            }

            int submeshIndex = submeshes.Count;
            ResoniteMaterialBinding material = materialByIdentity[identity];
            submeshes.Add(new ResoniteMeshSubmesh(submeshIndex, material.MaterialKey, indices));
            materials.Add(material with { SubmeshIndices = [submeshIndex] });
        }

        BakedOutputCityObjectCount++;
        return new ResoniteConstructionCityObject(
            SlotKey: CreateBatchSlotKey(cellKey, flushSequence),
            DisplayName: CreateBatchDisplayName(cellKey, flushSequence),
            PackageName: cellKey.PackageName,
            ActualMeshCode: cellKey.ActualMeshCode,
            LodLevel: cellKey.LodLevel,
            Transform: new ResoniteTransform(bakeOrigin),
            Mesh: new ResoniteImportedMesh(vertices, submeshes),
            Materials: materials,
            CollisionEnabled: buffer.CityObjects.Any(static cityObject => cityObject.CollisionEnabled),
            SourceObjectKey: CreateBatchSourceObjectKey(cellKey, flushSequence),
            SourceUnitKey: cellKey.SourceUnitKey);
    }

    private static ResoniteFloat3 ComputeBakeOrigin(CellBuffer buffer)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;

        foreach (ResoniteConstructionCityObject cityObject in buffer.CityObjects)
        {
            foreach (ResoniteMeshVertex vertex in cityObject.Mesh.Vertices)
            {
                ResoniteFloat3 worldPosition = Add(vertex.Position, cityObject.Transform.Position);
                minX = Math.Min(minX, worldPosition.X);
                minY = Math.Min(minY, worldPosition.Y);
                minZ = Math.Min(minZ, worldPosition.Z);
            }
        }

        if (double.IsPositiveInfinity(minX) || double.IsPositiveInfinity(minY) || double.IsPositiveInfinity(minZ))
        {
            return new ResoniteFloat3(0.0, 0.0, 0.0);
        }

        return new ResoniteFloat3(minX, minY, minZ);
    }

    private static string CreateBatchSlotKey(CellKey cellKey, int flushSequence)
    {
        string lodToken = cellKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        string sourceUnitToken = CreateSourceUnitToken(cellKey.SourceUnitKey);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"meshbake_{cellKey.PackageName}_{cellKey.ActualMeshCode}_{sourceUnitToken}_{lodToken}_{cellKey.CellX}_{cellKey.CellZ}_{flushSequence:D4}");
    }

    private static string CreateBatchDisplayName(CellKey cellKey, int flushSequence)
    {
        string lodToken = cellKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"MeshBake {cellKey.PackageName} LOD{lodToken} {cellKey.CellX},{cellKey.CellZ} #{flushSequence + 1}");
    }

    private static string CreateBatchSourceObjectKey(CellKey cellKey, int flushSequence)
    {
        string lodToken = cellKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        string sourceUnitToken = CreateSourceUnitToken(cellKey.SourceUnitKey);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"meshbake:{cellKey.ActualMeshCode}:{cellKey.PackageName}:{sourceUnitToken}:{lodToken}:{cellKey.CellX}:{cellKey.CellZ}:{flushSequence:D4}");
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

    private sealed class CellBuffer
    {
        public List<ResoniteConstructionCityObject> CityObjects { get; } = [];

        public int VertexCount { get; set; }

        public long LastTouchedSequence { get; set; }
    }

    private sealed record CellKey(
        string ActualMeshCode,
        string PackageName,
        int? LodLevel,
        string SourceUnitKey,
        int CellX,
        int CellZ);

    private sealed record MaterialIdentity(
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
        ResoniteMaterialAssetScope AssetScope)
    {
        public static MaterialIdentity From(ResoniteMaterialBinding material)
        {
            return new MaterialIdentity(
                material.MaterialKey,
                material.BaseColor,
                material.MaterialType,
                material.TexturePath,
                material.TextureSourceKind,
                material.Projection,
                material.DepthOffset,
                material.TextureScale,
                material.Family,
                material.TextureOffset,
                material.AssetScope);
        }
    }

    private sealed class CellKeyComparer : IComparer<CellKey>
    {
        public static CellKeyComparer Instance { get; } = new();

        public int Compare(CellKey? x, CellKey? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

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

            compare = string.CompareOrdinal(x.SourceUnitKey, y.SourceUnitKey);
            if (compare != 0)
            {
                return compare;
            }

            compare = x.CellX.CompareTo(y.CellX);
            if (compare != 0)
            {
                return compare;
            }

            return x.CellZ.CompareTo(y.CellZ);
        }
    }

    private sealed class MaterialIdentityComparer : IComparer<MaterialIdentity>
    {
        public static MaterialIdentityComparer Instance { get; } = new();

        public int Compare(MaterialIdentity? x, MaterialIdentity? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            int compare = string.CompareOrdinal(x.MaterialKey, y.MaterialKey);
            if (compare != 0)
            {
                return compare;
            }

            compare = x.MaterialType.CompareTo(y.MaterialType);
            if (compare != 0)
            {
                return compare;
            }

            compare = string.CompareOrdinal(x.TexturePath, y.TexturePath);
            if (compare != 0)
            {
                return compare;
            }

            compare = x.TextureSourceKind.CompareTo(y.TextureSourceKind);
            if (compare != 0)
            {
                return compare;
            }

            compare = x.Projection.CompareTo(y.Projection);
            if (compare != 0)
            {
                return compare;
            }

            compare = string.CompareOrdinal(x.Family, y.Family);
            if (compare != 0)
            {
                return compare;
            }

            compare = x.AssetScope.CompareTo(y.AssetScope);
            if (compare != 0)
            {
                return compare;
            }

            compare = CompareNullableFloat2(x.TextureScale, y.TextureScale);
            if (compare != 0)
            {
                return compare;
            }

            compare = CompareNullableFloat2(x.TextureOffset, y.TextureOffset);
            if (compare != 0)
            {
                return compare;
            }

            compare = CompareNullableDepthOffset(x.DepthOffset, y.DepthOffset);
            if (compare != 0)
            {
                return compare;
            }

            return CompareColor(x.BaseColor, y.BaseColor);
        }

        private static int CompareNullableFloat2(ResoniteFloat2? x, ResoniteFloat2? y)
        {
            if (x is null && y is null)
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            int compare = x.X.CompareTo(y.X);
            return compare != 0 ? compare : x.Y.CompareTo(y.Y);
        }

        private static int CompareNullableDepthOffset(ResoniteMaterialDepthOffset? x, ResoniteMaterialDepthOffset? y)
        {
            if (x is null && y is null)
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            int compare = x.Factor.CompareTo(y.Factor);
            return compare != 0 ? compare : x.Units.CompareTo(y.Units);
        }

        private static int CompareColor(ResoniteColor x, ResoniteColor y)
        {
            int compare = x.R.CompareTo(y.R);
            if (compare != 0)
            {
                return compare;
            }

            compare = x.G.CompareTo(y.G);
            if (compare != 0)
            {
                return compare;
            }

            compare = x.B.CompareTo(y.B);
            return compare != 0 ? compare : x.A.CompareTo(y.A);
        }
    }
}
