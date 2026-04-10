#pragma warning disable IDE0032

namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteMaterialBinding
{
    private string materialKey = string.Empty;
    private ResoniteColor baseColor = null!;
    private ResoniteMaterialType materialType;
    private ResoniteTextureSourceKind textureSourceKind;
    private ResoniteMaterialProjection projection;
    private IReadOnlyList<int> submeshIndices = Array.Empty<int>();

    public ResoniteMaterialBinding(
        string MaterialKey,
        ResoniteColor BaseColor,
        ResoniteMaterialType MaterialType,
        string? TexturePath,
        ResoniteTextureSourceKind TextureSourceKind,
        ResoniteMaterialProjection Projection,
        ResoniteMaterialDepthOffset? DepthOffset,
        IReadOnlyList<int> SubmeshIndices,
        ResoniteFloat2? TextureScale = null,
        string? Family = null,
        ResoniteFloat2? TextureOffset = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(MaterialKey);
        ArgumentNullException.ThrowIfNull(BaseColor);

        if (!Enum.IsDefined(MaterialType))
        {
            throw new ArgumentOutOfRangeException(nameof(MaterialType), MaterialType, "Unsupported material type.");
        }

        if (!Enum.IsDefined(TextureSourceKind))
        {
            throw new ArgumentOutOfRangeException(nameof(TextureSourceKind), TextureSourceKind, "Unsupported texture source kind.");
        }

        if (!Enum.IsDefined(Projection))
        {
            throw new ArgumentOutOfRangeException(nameof(Projection), Projection, "Unsupported material projection.");
        }

        this.MaterialKey = MaterialKey;
        this.BaseColor = BaseColor;
        this.MaterialType = MaterialType;
        this.TexturePath = TexturePath;
        this.TextureSourceKind = TextureSourceKind;
        this.Projection = Projection;
        this.DepthOffset = DepthOffset;
        this.SubmeshIndices = SubmeshIndices;
        this.TextureScale = TextureScale;
        this.Family = Family;
        this.TextureOffset = TextureOffset;
    }

    public string MaterialKey
    {
        get => materialKey;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            materialKey = value;
        }
    }

    public ResoniteColor BaseColor
    {
        get => baseColor;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            baseColor = value;
        }
    }

    public ResoniteMaterialType MaterialType
    {
        get => materialType;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(MaterialType), value, "Unsupported material type.");
            }

            materialType = value;
        }
    }

    public string? TexturePath { get; init; }

    public ResoniteTextureSourceKind TextureSourceKind
    {
        get => textureSourceKind;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(TextureSourceKind), value, "Unsupported texture source kind.");
            }

            textureSourceKind = value;
        }
    }

    public ResoniteMaterialProjection Projection
    {
        get => projection;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(Projection), value, "Unsupported material projection.");
            }

            projection = value;
        }
    }

    public ResoniteMaterialDepthOffset? DepthOffset { get; init; }

    public IReadOnlyList<int> SubmeshIndices
    {
        get => submeshIndices;
        init
        {
            IReadOnlyList<int> copied = CollectionCopy.List(value, nameof(SubmeshIndices));
            if (copied.Count == 0)
            {
                throw new ArgumentException("At least one submesh index is required.", nameof(SubmeshIndices));
            }

            HashSet<int> seenIndices = [];
            foreach (int submeshIndex in copied)
            {
                if (submeshIndex < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(SubmeshIndices),
                        submeshIndex,
                        "Submesh indices cannot be negative.");
                }

                if (!seenIndices.Add(submeshIndex))
                {
                    throw new ArgumentException(
                        "Submesh indices cannot contain duplicates.",
                        nameof(SubmeshIndices));
                }
            }

            submeshIndices = copied;
        }
    }

    public ResoniteFloat2? TextureScale { get; init; }

    public string? Family { get; init; }

    public ResoniteFloat2? TextureOffset { get; init; }

    public void Deconstruct(
        out string MaterialKey,
        out ResoniteColor BaseColor,
        out ResoniteMaterialType MaterialType,
        out string? TexturePath,
        out ResoniteTextureSourceKind TextureSourceKind,
        out ResoniteMaterialProjection Projection,
        out ResoniteMaterialDepthOffset? DepthOffset,
        out IReadOnlyList<int> SubmeshIndices,
        out ResoniteFloat2? TextureScale,
        out string? Family,
        out ResoniteFloat2? TextureOffset)
    {
        MaterialKey = this.MaterialKey;
        BaseColor = this.BaseColor;
        MaterialType = this.MaterialType;
        TexturePath = this.TexturePath;
        TextureSourceKind = this.TextureSourceKind;
        Projection = this.Projection;
        DepthOffset = this.DepthOffset;
        SubmeshIndices = this.SubmeshIndices;
        TextureScale = this.TextureScale;
        Family = this.Family;
        TextureOffset = this.TextureOffset;
    }
}

#pragma warning restore IDE0032
