#pragma warning disable IDE0032

namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteConstructionMetadata
{
    private string schemaVersion = string.Empty;
    private string worldName = string.Empty;
    private PlateauImportRequest request = null!;
    private PlateauSourceDataset sourceDataset = null!;
    private ResoniteAttribution attribution = null!;
    private ResoniteLocalOrigin localOrigin = null!;

    public ResoniteConstructionMetadata(
        string SchemaVersion,
        string WorldName,
        PlateauImportRequest Request,
        PlateauSourceDataset SourceDataset,
        ResoniteAttribution Attribution,
        ResoniteLocalOrigin LocalOrigin)
    {
        this.SchemaVersion = SchemaVersion;
        this.WorldName = WorldName;
        this.Request = Request;
        this.SourceDataset = SourceDataset;
        this.Attribution = Attribution;
        this.LocalOrigin = LocalOrigin;
    }

    public string SchemaVersion
    {
        get => schemaVersion;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            schemaVersion = value;
        }
    }

    public string WorldName
    {
        get => worldName;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            worldName = value;
        }
    }

    public PlateauImportRequest Request
    {
        get => request;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            request = value;
        }
    }

    public PlateauSourceDataset SourceDataset
    {
        get => sourceDataset;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            sourceDataset = value;
        }
    }

    public ResoniteAttribution Attribution
    {
        get => attribution;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            attribution = value;
        }
    }

    public ResoniteLocalOrigin LocalOrigin
    {
        get => localOrigin;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            localOrigin = value;
        }
    }

    public void Deconstruct(
        out string SchemaVersion,
        out string WorldName,
        out PlateauImportRequest Request,
        out PlateauSourceDataset SourceDataset,
        out ResoniteAttribution Attribution,
        out ResoniteLocalOrigin LocalOrigin)
    {
        SchemaVersion = this.SchemaVersion;
        WorldName = this.WorldName;
        Request = this.Request;
        SourceDataset = this.SourceDataset;
        Attribution = this.Attribution;
        LocalOrigin = this.LocalOrigin;
    }
}

#pragma warning restore IDE0032
