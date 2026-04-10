#pragma warning disable IDE0032

namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteConstructionCityObject
{
    private string slotKey = string.Empty;
    private string displayName = string.Empty;
    private string packageName = string.Empty;
    private string actualMeshCode = string.Empty;
    private ResoniteTransform transform = null!;
    private ResoniteConstructionGeometry geometry = null!;
    private IReadOnlyList<ResoniteMaterialBinding> materials = Array.Empty<ResoniteMaterialBinding>();

    public ResoniteConstructionCityObject(
        string SlotKey,
        string DisplayName,
        string PackageName,
        string ActualMeshCode,
        int? LodLevel,
        ResoniteTransform Transform,
        ResoniteConstructionGeometry Geometry,
        IReadOnlyList<ResoniteMaterialBinding> Materials,
        bool CollisionEnabled = true,
        string? SourceObjectKey = null)
    {
        this.SlotKey = SlotKey;
        this.DisplayName = DisplayName;
        this.PackageName = PackageName;
        this.ActualMeshCode = ActualMeshCode;
        this.LodLevel = LodLevel;
        this.Transform = Transform;
        this.Geometry = Geometry;
        this.Materials = Materials;
        this.CollisionEnabled = CollisionEnabled;
        this.SourceObjectKey = SourceObjectKey;
    }

    public ResoniteConstructionCityObject(
        string SlotKey,
        string DisplayName,
        string PackageName,
        string ActualMeshCode,
        int? LodLevel,
        ResoniteTransform Transform,
        ResoniteImportedMesh Mesh,
        IReadOnlyList<ResoniteMaterialBinding> Materials,
        bool CollisionEnabled = true,
        string? SourceObjectKey = null)
        : this(
            SlotKey,
            DisplayName,
            PackageName,
            ActualMeshCode,
            LodLevel,
            Transform,
            new ResoniteTriangleMeshGeometry(Mesh),
            Materials,
            CollisionEnabled,
            SourceObjectKey)
    {
    }

    public string SlotKey
    {
        get => slotKey;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            slotKey = value;
        }
    }

    public string DisplayName
    {
        get => displayName;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            displayName = value;
        }
    }

    public string PackageName
    {
        get => packageName;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            packageName = value;
        }
    }

    public string ActualMeshCode
    {
        get => actualMeshCode;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            actualMeshCode = value;
        }
    }

    public int? LodLevel { get; init; }

    public ResoniteTransform Transform
    {
        get => transform;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            transform = value;
        }
    }

    public ResoniteConstructionGeometry Geometry
    {
        get => geometry;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            geometry = value;
        }
    }

    public IReadOnlyList<ResoniteMaterialBinding> Materials
    {
        get => materials;
        init => materials = CollectionCopy.List(value, nameof(Materials));
    }

    public bool CollisionEnabled { get; init; }

    public string? SourceObjectKey { get; init; }

    public void Deconstruct(
        out string SlotKey,
        out string DisplayName,
        out string PackageName,
        out string ActualMeshCode,
        out int? LodLevel,
        out ResoniteTransform Transform,
        out ResoniteConstructionGeometry Geometry,
        out IReadOnlyList<ResoniteMaterialBinding> Materials,
        out bool CollisionEnabled,
        out string? SourceObjectKey)
    {
        SlotKey = this.SlotKey;
        DisplayName = this.DisplayName;
        PackageName = this.PackageName;
        ActualMeshCode = this.ActualMeshCode;
        LodLevel = this.LodLevel;
        Transform = this.Transform;
        Geometry = this.Geometry;
        Materials = this.Materials;
        CollisionEnabled = this.CollisionEnabled;
        SourceObjectKey = this.SourceObjectKey;
    }

    public ResoniteImportedMesh Mesh => Geometry is ResoniteTriangleMeshGeometry triangleMesh
        ? triangleMesh.Mesh
        : throw new System.InvalidOperationException("This city object does not use triangle mesh geometry.");
}

#pragma warning restore IDE0032
