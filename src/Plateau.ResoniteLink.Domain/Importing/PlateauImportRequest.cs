#pragma warning disable IDE0032

namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record PlateauImportRequest
{
    private string dataset = string.Empty;
    private string meshCode = string.Empty;
    private PlateauImportSource source = null!;
    private IReadOnlyList<string>? packageNames;
    private IReadOnlySet<int>? globalExcludeLodLevels;
    private IReadOnlyDictionary<string, IReadOnlySet<int>>? excludeLodLevelsByPackage;
    private IReadOnlyDictionary<string, string>? packagePatterns;
    private DemTerrainMode demTerrainMode = DemTerrainMode.Mesh;
    private double demHeightmapMetersPerVertex = 2.0;
    private int demHeightmapMaxResolution = 1024;

    public PlateauImportRequest(
        string Dataset,
        string MeshCode,
        PlateauImportSource Source,
        IReadOnlyList<string>? PackageNames = null,
        IReadOnlySet<int>? GlobalExcludeLodLevels = null,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? ExcludeLodLevelsByPackage = null,
        IReadOnlyDictionary<string, string>? PackagePatterns = null,
        bool IncludeMarkingAlways = true,
        DemTerrainMode DemTerrainMode = DemTerrainMode.Mesh,
        double DemHeightmapMetersPerVertex = 2.0,
        int DemHeightmapMaxResolution = 1024)
    {
        this.Dataset = Dataset;
        this.MeshCode = MeshCode;
        this.Source = Source;
        this.PackageNames = CollectionCopy.ListOrNull(PackageNames);
        this.GlobalExcludeLodLevels = CollectionCopy.SetOrNull(GlobalExcludeLodLevels);
        this.ExcludeLodLevelsByPackage = CollectionCopy.NestedSetDictionaryOrNull(ExcludeLodLevelsByPackage);
        this.PackagePatterns = CollectionCopy.DictionaryOrNull(PackagePatterns);
        this.IncludeMarkingAlways = IncludeMarkingAlways;
        this.DemTerrainMode = DemTerrainMode;
        this.DemHeightmapMetersPerVertex = DemHeightmapMetersPerVertex;
        this.DemHeightmapMaxResolution = DemHeightmapMaxResolution;
    }

    public PlateauImportRequest(
        string Dataset,
        string MeshCode,
        DatasetSourceKind SourceKind,
        string? LocalSourcePath,
        Uri? ServerUri,
        IReadOnlyList<string>? PackageNames = null,
        IReadOnlySet<int>? GlobalExcludeLodLevels = null,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? ExcludeLodLevelsByPackage = null,
        IReadOnlyDictionary<string, string>? PackagePatterns = null,
        bool IncludeMarkingAlways = true,
        DemTerrainMode DemTerrainMode = DemTerrainMode.Mesh,
        double DemHeightmapMetersPerVertex = 2.0,
        int DemHeightmapMaxResolution = 1024)
        : this(
            Dataset,
            MeshCode,
            PlateauImportSource.FromLegacy(SourceKind, LocalSourcePath, ServerUri),
            PackageNames,
            GlobalExcludeLodLevels,
            ExcludeLodLevelsByPackage,
            PackagePatterns,
            IncludeMarkingAlways,
            DemTerrainMode,
            DemHeightmapMetersPerVertex,
            DemHeightmapMaxResolution)
    {
    }

    public string Dataset
    {
        get => dataset;
        init
        {
            dataset = value ?? string.Empty;
        }
    }

    public string MeshCode
    {
        get => meshCode;
        init
        {
            meshCode = value ?? string.Empty;
        }
    }

    public PlateauImportSource Source
    {
        get => source;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            source = value;
        }
    }

    public IReadOnlyList<string>? PackageNames
    {
        get => packageNames;
        init
        {
            packageNames = CollectionCopy.ListOrNull(value);
        }
    }

    public IReadOnlySet<int>? GlobalExcludeLodLevels
    {
        get => globalExcludeLodLevels;
        init
        {
            globalExcludeLodLevels = CollectionCopy.SetOrNull(value);
        }
    }

    public IReadOnlyDictionary<string, IReadOnlySet<int>>? ExcludeLodLevelsByPackage
    {
        get => excludeLodLevelsByPackage;
        init
        {
            excludeLodLevelsByPackage = CollectionCopy.NestedSetDictionaryOrNull(value);
        }
    }

    public IReadOnlyDictionary<string, string>? PackagePatterns
    {
        get => packagePatterns;
        init
        {
            packagePatterns = CollectionCopy.DictionaryOrNull(value);
        }
    }

    public bool IncludeMarkingAlways { get; init; }

    public DemTerrainMode DemTerrainMode
    {
        get => demTerrainMode;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(DemTerrainMode), value, "DemTerrainMode must be a defined enum value.");
            }

            demTerrainMode = value;
        }
    }

    public double DemHeightmapMetersPerVertex
    {
        get => demHeightmapMetersPerVertex;
        init
        {
            if (value <= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(DemHeightmapMetersPerVertex),
                    value,
                    "DemHeightmapMetersPerVertex must be positive.");
            }

            demHeightmapMetersPerVertex = value;
        }
    }

    public int DemHeightmapMaxResolution
    {
        get => demHeightmapMaxResolution;
        init
        {
            if (value < 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(DemHeightmapMaxResolution),
                    value,
                    "DemHeightmapMaxResolution must be 2 or greater.");
            }

            demHeightmapMaxResolution = value;
        }
    }

    public DatasetSourceKind SourceKind => Source.SourceKind;

    public string? LocalSourcePath => Source is PlateauLocalImportSource localSource ? localSource.LocalSourcePath : null;

    public Uri? ServerUri => Source is PlateauRemoteImportSource remoteSource ? remoteSource.ServerUri : null;

    public void Deconstruct(
        out string Dataset,
        out string MeshCode,
        out PlateauImportSource Source,
        out IReadOnlyList<string>? PackageNames,
        out IReadOnlySet<int>? GlobalExcludeLodLevels,
        out IReadOnlyDictionary<string, IReadOnlySet<int>>? ExcludeLodLevelsByPackage,
        out IReadOnlyDictionary<string, string>? PackagePatterns,
        out bool IncludeMarkingAlways,
        out DemTerrainMode DemTerrainMode,
        out double DemHeightmapMetersPerVertex,
        out int DemHeightmapMaxResolution)
    {
        Dataset = this.Dataset;
        MeshCode = this.MeshCode;
        Source = this.Source;
        PackageNames = this.PackageNames;
        GlobalExcludeLodLevels = this.GlobalExcludeLodLevels;
        ExcludeLodLevelsByPackage = this.ExcludeLodLevelsByPackage;
        PackagePatterns = this.PackagePatterns;
        IncludeMarkingAlways = this.IncludeMarkingAlways;
        DemTerrainMode = this.DemTerrainMode;
        DemHeightmapMetersPerVertex = this.DemHeightmapMetersPerVertex;
        DemHeightmapMaxResolution = this.DemHeightmapMaxResolution;
    }
}

#pragma warning restore IDE0032
