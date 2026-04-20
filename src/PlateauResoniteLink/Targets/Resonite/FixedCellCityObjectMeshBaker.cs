using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class FixedCellCityObjectMeshBaker : IResoniteBufferedCityObjectBaker
{
    internal const double DefaultCellSizeMeters = 128.0;
    internal const int DefaultMaxCityObjectsPerBatch = 64;
    internal const int DefaultMaxVerticesPerBatch = 200_000;
    internal const int DefaultMaxBufferedCells = 8;

    private readonly double cellSizeMeters;
    private readonly int maxCityObjectsPerBatch;
    private readonly int maxVerticesPerBatch;
    private readonly int maxBufferedCells;
    private readonly Dictionary<CellKey, CellBuffer> buffers = [];
    private readonly Dictionary<CellKey, int> flushSequenceByCell = [];
    private readonly LinkedList<CellKey> bufferedCellOrder = [];
    private readonly Dictionary<CellKey, LinkedListNode<CellKey>> bufferedCellNodes = [];

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

    public string Name => "LOD1MeshBake";

    public int BakedInputCityObjectCount { get; private set; }

    public int BakedOutputCityObjectCount { get; private set; }

    public bool TryBuffer(
        ResoniteConstructionCityObject cityObject,
        out ResoniteConstructionCityObject? bakedCityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        cityObject = ResoniteDynamicMaterialUvNormalizer.Normalize(cityObject);

        bakedCityObject = null;
        if (!CanBake(cityObject))
        {
            return false;
        }

        CellKey cellKey = CreateCellKey(cityObject);
        bool createdBuffer = false;
        if (!buffers.TryGetValue(cellKey, out CellBuffer? buffer))
        {
            buffer = new CellBuffer();
            buffers.Add(cellKey, buffer);
            createdBuffer = true;
        }

        List<ResoniteConstructionCityObject> readyCityObjects = BufferCore(cityObject, cellKey, buffer, createdBuffer);
        bakedCityObject = readyCityObjects.Count > 0 ? readyCityObjects[0] : null;
        return true;
    }

    public ValueTask<BufferedCityObjectBufferResult> TryBufferAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        cityObject = ResoniteDynamicMaterialUvNormalizer.Normalize(cityObject);

        if (!CanBake(cityObject))
        {
            return ValueTask.FromResult(new BufferedCityObjectBufferResult(Buffered: false, []));
        }

        CellKey cellKey = CreateCellKey(cityObject);
        bool createdBuffer = false;
        if (!buffers.TryGetValue(cellKey, out CellBuffer? buffer))
        {
            buffer = new CellBuffer();
            buffers.Add(cellKey, buffer);
            createdBuffer = true;
        }

        List<ResoniteConstructionCityObject> readyCityObjects = BufferCore(cityObject, cellKey, buffer, createdBuffer);
        return ValueTask.FromResult(new BufferedCityObjectBufferResult(Buffered: true, readyCityObjects));
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
        bufferedCellOrder.Clear();
        bufferedCellNodes.Clear();
        return bakedCityObjects;
    }

    public Task<IReadOnlyList<ResoniteConstructionCityObject>> FlushAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(FlushAll());
    }

    public async Task FlushAllAsync(
        Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onBakedCityObject);
        if (buffers.Count == 0)
        {
            return;
        }

        CellKey[] orderedCellKeys = buffers.Keys
            .OrderBy(static cellKey => cellKey, CellKeyComparer.Instance)
            .ToArray();
        foreach (CellKey cellKey in orderedCellKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!buffers.Remove(cellKey, out CellBuffer? buffer))
            {
                continue;
            }

            DetachBufferedCell(cellKey);
            ResoniteConstructionCityObject bakedCityObject = BakeCell(cellKey, buffer);
            buffer.CityObjects.Clear();
            await onBakedCityObject(bakedCityObject, cancellationToken);
        }
    }

    private List<ResoniteConstructionCityObject> BufferCore(
        ResoniteConstructionCityObject cityObject,
        CellKey cellKey,
        CellBuffer buffer,
        bool createdBuffer)
    {
        TrackBufferedCellAccess(cellKey, createdBuffer);
        buffer.CityObjects.Add(cityObject);
        buffer.VertexCount += cityObject.Mesh.Vertices.Count;
        BakedInputCityObjectCount++;

        List<ResoniteConstructionCityObject> readyCityObjects = [];
        bool exceededCityObjectBudget = cityObject.SourceFileRelativePath is null
            && buffer.CityObjects.Count > maxCityObjectsPerBatch;
        if (exceededCityObjectBudget || buffer.VertexCount > maxVerticesPerBatch)
        {
            readyCityObjects.Add(FlushCell(cellKey));
        }

        while (buffers.Count > maxBufferedCells)
        {
            CellKey overflowCellKey = GetOldestBufferedCellKey(excluding: cellKey);
            readyCityObjects.Add(FlushCell(overflowCellKey));
        }

        return readyCityObjects;
    }

    private static bool CanBake(ResoniteConstructionCityObject cityObject)
    {
        return string.Equals(cityObject.PackageName, "bldg", StringComparison.OrdinalIgnoreCase)
            && cityObject.LodLevel == 1
            && cityObject.Geometry is ResoniteTriangleMeshGeometry
            && cityObject.Transform.Rotation is null;
    }

    private static CellKey CreateCellKey(ResoniteConstructionCityObject cityObject)
    {
        string scopeIdentity = cityObject.SourceFileRelativePath
            ?? cityObject.SourceUnitKey
            ?? cityObject.SourceObjectKey
            ?? cityObject.SlotKey;
        return new CellKey(
            cityObject.ActualMeshCode,
            cityObject.PackageName,
            cityObject.LodLevel,
            scopeIdentity,
            CellX: 0,
            CellZ: 0);
    }

    private ResoniteConstructionCityObject FlushCell(CellKey cellKey)
    {
        if (!buffers.Remove(cellKey, out CellBuffer? buffer))
        {
            throw new InvalidOperationException($"Cannot flush missing bake buffer '{cellKey}'.");
        }

        DetachBufferedCell(cellKey);
        return BakeCell(cellKey, buffer);
    }

    private void TrackBufferedCellAccess(CellKey cellKey, bool createdBuffer)
    {
        if (createdBuffer)
        {
            LinkedListNode<CellKey> node = bufferedCellOrder.AddLast(cellKey);
            bufferedCellNodes.Add(cellKey, node);
            return;
        }

        if (!bufferedCellNodes.TryGetValue(cellKey, out LinkedListNode<CellKey>? existingNode))
        {
            throw new InvalidOperationException($"Buffered cell order tracking is missing '{cellKey}'.");
        }

        bufferedCellOrder.Remove(existingNode);
        bufferedCellOrder.AddLast(existingNode);
    }

    private void DetachBufferedCell(CellKey cellKey)
    {
        if (!bufferedCellNodes.Remove(cellKey, out LinkedListNode<CellKey>? node))
        {
            return;
        }

        bufferedCellOrder.Remove(node);
    }

    private CellKey GetOldestBufferedCellKey(CellKey excluding)
    {
        LinkedListNode<CellKey>? node = bufferedCellOrder.First;
        while (node is not null)
        {
            if (!EqualityComparer<CellKey>.Default.Equals(node.Value, excluding))
            {
                return node.Value;
            }

            node = node.Next;
        }

        return excluding;
    }

    private ResoniteConstructionCityObject BakeCell(CellKey cellKey, CellBuffer buffer)
    {
        int flushSequence = flushSequenceByCell.GetValueOrDefault(cellKey);
        flushSequenceByCell[cellKey] = flushSequence + 1;
        string? sourceUnitKey = GetMergedSourceUnitKey(buffer.CityObjects);
        string? sourceFileRelativePath = GetMergedSourceFileRelativePath(buffer.CityObjects);

        ResoniteFloat3 bakeOrigin = ComputeBakeOrigin(buffer);
        List<ResoniteMeshVertex> vertices = [];
        Dictionary<MaterialIdentity, List<int>> trianglesByMaterial = [];
        Dictionary<MaterialIdentity, ResoniteMaterialBinding> materialByIdentity = [];

        foreach (ResoniteConstructionCityObject cityObject in buffer.CityObjects
                     .OrderBy(static candidate => candidate.SlotKey, StringComparer.Ordinal)
                     .ThenBy(static candidate => candidate.SourceObjectKey, StringComparer.Ordinal)
                     .ThenBy(static candidate => candidate.DisplayName, StringComparer.Ordinal))
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
                    throw new InvalidOperationException(
                        $"Buffered mesh bake city object '{cityObject.DisplayName}' left submesh index {submesh.Index} without a material assignment.");
                }

                ResoniteMaterialBinding normalizedMaterial = ResoniteSceneMaterialConventions.NormalizeBatchGroupedMaterialBinding(material);
                MaterialIdentity identity = MaterialIdentity.From(normalizedMaterial);
                materialByIdentity.TryAdd(identity, normalizedMaterial);
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

        // Keep the merged LOD1 mesh as a pure concatenation of source vertices and
        // triangles with only per-object position offsets applied. Reindexing or
        // vertex dedup here regressed Resonite StaticMesh import in live runs.
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

        if (submeshes.Count == 0 || materials.Count == 0)
        {
            throw new InvalidOperationException(
                $"Buffered mesh bake batch '{cellKey.PackageName}:{cellKey.ActualMeshCode}:LOD{(cellKey.LodLevel.HasValue ? cellKey.LodLevel.Value.ToString(CultureInfo.InvariantCulture) : "none")}' produced no materialized submesh.");
        }

        ValidateBakedMesh(buffer.CityObjects, vertices, submeshes, materials, cellKey);

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
            SourceUnitKey: sourceUnitKey,
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static void ValidateBakedMesh(
        IReadOnlyList<ResoniteConstructionCityObject> sourceCityObjects,
        List<ResoniteMeshVertex> bakedVertices,
        List<ResoniteMeshSubmesh> bakedSubmeshes,
        List<ResoniteMaterialBinding> bakedMaterials,
        CellKey cellKey)
    {
        int expectedVertexCount = sourceCityObjects.Sum(static cityObject => cityObject.Mesh.Vertices.Count);
        if (expectedVertexCount != bakedVertices.Count)
        {
            throw new InvalidOperationException(
                $"Buffered mesh bake batch '{cellKey.PackageName}:{cellKey.ActualMeshCode}' changed vertex count "
                + $"from {expectedVertexCount} to {bakedVertices.Count}.");
        }

        int expectedTriangleIndexCount = sourceCityObjects
            .SelectMany(static cityObject => cityObject.Mesh.Submeshes)
            .Sum(static submesh => submesh.TriangleVertexIndices.Count);
        int actualTriangleIndexCount = bakedSubmeshes.Sum(static submesh => submesh.TriangleVertexIndices.Count);
        if (expectedTriangleIndexCount != actualTriangleIndexCount)
        {
            throw new InvalidOperationException(
                $"Buffered mesh bake batch '{cellKey.PackageName}:{cellKey.ActualMeshCode}' changed triangle index count "
                + $"from {expectedTriangleIndexCount} to {actualTriangleIndexCount}.");
        }

        int expectedMaterialCount = sourceCityObjects
            .SelectMany(static cityObject => cityObject.Materials)
            .Select(MaterialIdentity.From)
            .Distinct()
            .Count();
        if (expectedMaterialCount != bakedMaterials.Count)
        {
            throw new InvalidOperationException(
                $"Buffered mesh bake batch '{cellKey.PackageName}:{cellKey.ActualMeshCode}' changed material group count "
                + $"from {expectedMaterialCount} to {bakedMaterials.Count}.");
        }

        Dictionary<MaterialIdentity, int> expectedTriangleIndexCountByMaterial = SummarizeSourceTriangleIndicesByMaterial(sourceCityObjects);
        Dictionary<MaterialIdentity, int> actualTriangleIndexCountByMaterial = SummarizeBakedTriangleIndicesByMaterial(
            bakedSubmeshes,
            bakedMaterials);
        if (expectedTriangleIndexCountByMaterial.Count != actualTriangleIndexCountByMaterial.Count)
        {
            throw new InvalidOperationException(
                $"Buffered mesh bake batch '{cellKey.PackageName}:{cellKey.ActualMeshCode}' changed materialized submesh count "
                + $"from {expectedTriangleIndexCountByMaterial.Count} to {actualTriangleIndexCountByMaterial.Count}.");
        }

        foreach ((MaterialIdentity identity, int expectedIndexCount) in expectedTriangleIndexCountByMaterial)
        {
            if (!actualTriangleIndexCountByMaterial.TryGetValue(identity, out int actualIndexCount))
            {
                throw new InvalidOperationException(
                    $"Buffered mesh bake batch '{cellKey.PackageName}:{cellKey.ActualMeshCode}' lost materialized submesh for '{identity.MaterialKey}'.");
            }

            if (expectedIndexCount != actualIndexCount)
            {
                throw new InvalidOperationException(
                    $"Buffered mesh bake batch '{cellKey.PackageName}:{cellKey.ActualMeshCode}' changed triangle index count for material "
                    + $"'{identity.MaterialKey}' from {expectedIndexCount} to {actualIndexCount}.");
            }
        }

        if (bakedSubmeshes.Count != bakedMaterials.Count)
        {
            throw new InvalidOperationException(
                $"Buffered mesh bake batch '{cellKey.PackageName}:{cellKey.ActualMeshCode}' created {bakedSubmeshes.Count} submeshes for {bakedMaterials.Count} materials.");
        }

        Dictionary<int, string> materialKeyBySubmeshIndex = [];
        foreach (ResoniteMaterialBinding material in bakedMaterials)
        {
            if (material.SubmeshIndices.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Buffered mesh bake batch '{cellKey.PackageName}:{cellKey.ActualMeshCode}' material '{material.MaterialKey}' targeted {material.SubmeshIndices.Count} submeshes.");
            }

            int submeshIndex = material.SubmeshIndices[0];
            if ((uint)submeshIndex >= (uint)bakedSubmeshes.Count)
            {
                throw new InvalidOperationException(
                    $"Buffered mesh bake batch '{cellKey.PackageName}:{cellKey.ActualMeshCode}' material '{material.MaterialKey}' targeted missing submesh index {submeshIndex}.");
            }

            if (!materialKeyBySubmeshIndex.TryAdd(submeshIndex, material.MaterialKey))
            {
                throw new InvalidOperationException(
                    $"Buffered mesh bake batch '{cellKey.PackageName}:{cellKey.ActualMeshCode}' assigned submesh index {submeshIndex} multiple times.");
            }

            if (!string.Equals(bakedSubmeshes[submeshIndex].MaterialKey, material.MaterialKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Buffered mesh bake batch '{cellKey.PackageName}:{cellKey.ActualMeshCode}' mismatched material key '{material.MaterialKey}' and submesh key '{bakedSubmeshes[submeshIndex].MaterialKey}' at submesh index {submeshIndex}.");
            }
        }

        for (int submeshIndex = 0; submeshIndex < bakedSubmeshes.Count; submeshIndex++)
        {
            if (!materialKeyBySubmeshIndex.ContainsKey(submeshIndex))
            {
                throw new InvalidOperationException(
                    $"Buffered mesh bake batch '{cellKey.PackageName}:{cellKey.ActualMeshCode}' left submesh index {submeshIndex} without a material assignment.");
            }
        }

        for (int submeshIndex = 0; submeshIndex < bakedSubmeshes.Count; submeshIndex++)
        {
            ResoniteMeshSubmesh submesh = bakedSubmeshes[submeshIndex];
            if (submesh.TriangleVertexIndices.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Buffered mesh bake batch '{cellKey.PackageName}:{cellKey.ActualMeshCode}' produced empty submesh index {submeshIndex}.");
            }

            if (submesh.TriangleVertexIndices.Count % 3 != 0)
            {
                throw new InvalidOperationException(
                    $"Buffered mesh bake batch '{cellKey.PackageName}:{cellKey.ActualMeshCode}' produced submesh index {submeshIndex} with {submesh.TriangleVertexIndices.Count} indices, which is not divisible by three.");
            }

            foreach (int vertexIndex in submesh.TriangleVertexIndices)
            {
                if ((uint)vertexIndex >= (uint)bakedVertices.Count)
                {
                    throw new InvalidOperationException(
                        $"Buffered mesh bake batch '{cellKey.PackageName}:{cellKey.ActualMeshCode}' produced submesh index {submeshIndex} with out-of-range vertex index {vertexIndex}, vertex_count={bakedVertices.Count}.");
                }
            }
        }
    }

    private static Dictionary<MaterialIdentity, int> SummarizeSourceTriangleIndicesByMaterial(
        IReadOnlyList<ResoniteConstructionCityObject> sourceCityObjects)
    {
        Dictionary<MaterialIdentity, int> counts = [];
        foreach (ResoniteConstructionCityObject cityObject in sourceCityObjects)
        {
            Dictionary<int, ResoniteMaterialBinding> materialBySubmeshIndex = cityObject.Materials
                .SelectMany(material => material.SubmeshIndices.Select(submeshIndex => (submeshIndex, material)))
                .ToDictionary(static pair => pair.submeshIndex, static pair => pair.material);

            foreach (ResoniteMeshSubmesh submesh in cityObject.Mesh.Submeshes)
            {
                if (!materialBySubmeshIndex.TryGetValue(submesh.Index, out ResoniteMaterialBinding? material))
                {
                    throw new InvalidOperationException(
                        $"Buffered mesh bake source city object '{cityObject.DisplayName}' left submesh index {submesh.Index} without a material assignment.");
                }

                MaterialIdentity identity = MaterialIdentity.From(material);
                counts[identity] = counts.GetValueOrDefault(identity) + submesh.TriangleVertexIndices.Count;
            }
        }

        return counts;
    }

    private static Dictionary<MaterialIdentity, int> SummarizeBakedTriangleIndicesByMaterial(
        List<ResoniteMeshSubmesh> bakedSubmeshes,
        List<ResoniteMaterialBinding> bakedMaterials)
    {
        Dictionary<MaterialIdentity, int> counts = [];
        foreach (ResoniteMaterialBinding material in bakedMaterials)
        {
            if (material.SubmeshIndices.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Buffered mesh bake material '{material.MaterialKey}' targeted {material.SubmeshIndices.Count} submeshes.");
            }

            int submeshIndex = material.SubmeshIndices[0];
            if ((uint)submeshIndex >= (uint)bakedSubmeshes.Count)
            {
                throw new InvalidOperationException(
                    $"Buffered mesh bake material '{material.MaterialKey}' targeted missing submesh index {submeshIndex}.");
            }

            MaterialIdentity identity = MaterialIdentity.From(material);
            counts[identity] = bakedSubmeshes[submeshIndex].TriangleVertexIndices.Count;
        }

        return counts;
    }

    private static string? GetMergedSourceUnitKey(IEnumerable<ResoniteConstructionCityObject> cityObjects)
    {
        HashSet<string?> sourceUnitKeys = [];
        foreach (ResoniteConstructionCityObject cityObject in cityObjects)
        {
            sourceUnitKeys.Add(cityObject.SourceUnitKey);
        }

        if (sourceUnitKeys.Count == 0)
        {
            return null;
        }

        if (sourceUnitKeys.Count == 1)
        {
            return sourceUnitKeys.Single();
        }

        return null;
    }

    private static string? GetMergedSourceFileRelativePath(IEnumerable<ResoniteConstructionCityObject> cityObjects)
    {
        HashSet<string?> sourceFileRelativePaths = [];
        foreach (ResoniteConstructionCityObject cityObject in cityObjects)
        {
            sourceFileRelativePaths.Add(cityObject.SourceFileRelativePath);
        }

        if (sourceFileRelativePaths.Count == 0)
        {
            return null;
        }

        if (sourceFileRelativePaths.Count == 1)
        {
            return sourceFileRelativePaths.Single();
        }

        return null;
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
        string sourceUnitToken = CreateSourceUnitToken(cellKey.ScopeIdentity);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"meshbake_{cellKey.PackageName}_{cellKey.ActualMeshCode}_{sourceUnitToken}_{lodToken}_{cellKey.CellX}_{cellKey.CellZ}_{flushSequence:D4}");
    }

    private static string CreateBatchDisplayName(CellKey cellKey, int flushSequence)
    {
        string lodToken = cellKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        string sourceUnitToken = CreateSourceUnitToken(cellKey.ScopeIdentity);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"MeshBake {cellKey.PackageName} LOD{lodToken} {sourceUnitToken} #{flushSequence + 1}");
    }

    private static string CreateBatchSourceObjectKey(CellKey cellKey, int flushSequence)
    {
        string lodToken = cellKey.LodLevel?.ToString(CultureInfo.InvariantCulture) ?? "none";
        string sourceUnitToken = CreateSourceUnitToken(cellKey.ScopeIdentity);
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

        public void Clear()
        {
            CityObjects.Clear();
            VertexCount = 0;
        }
    }

    private sealed record CellKey(
        string ActualMeshCode,
        string PackageName,
        int? LodLevel,
        string ScopeIdentity,
        int CellX,
        int CellZ);

    private sealed record MaterialIdentity(
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
        ResoniteMaterialAssetScope AssetScope)
    {
        public static MaterialIdentity From(ResoniteMaterialBinding material)
        {
            ResoniteMaterialBinding normalizedMaterial = ResoniteSceneMaterialConventions.NormalizeBatchGroupedMaterialBinding(material);
            return new MaterialIdentity(
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

            compare = string.CompareOrdinal(x.ScopeIdentity, y.ScopeIdentity);
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

            compare = string.CompareOrdinal(x.TextureIdentity, y.TextureIdentity);
            if (compare != 0)
            {
                return compare;
            }

            compare = x.TextureSourceKind.CompareTo(y.TextureSourceKind);
            if (compare != 0)
            {
                return compare;
            }

            compare = string.CompareOrdinal(
                CreateTerrainOverlaySortKey(x.TerrainOverlay),
                CreateTerrainOverlaySortKey(y.TerrainOverlay));
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

        private static string? CreateTerrainOverlaySortKey(TerrainTextureOverlay? overlay)
        {
            return overlay is null
                ? null
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{overlay.PackageName}|{overlay.GeographicBounds.MinLatitude:R}|{overlay.GeographicBounds.MinLongitude:R}|{overlay.GeographicBounds.MaxLatitude:R}|{overlay.GeographicBounds.MaxLongitude:R}|{overlay.SourceIdentityKey}|{overlay.MaxTextureSize}");
        }
    }

}
