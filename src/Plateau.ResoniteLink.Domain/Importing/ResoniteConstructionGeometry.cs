#pragma warning disable IDE0032

namespace Plateau.ResoniteLink.Domain.Importing;

public abstract record ResoniteConstructionGeometry;

public sealed record ResoniteTriangleMeshGeometry : ResoniteConstructionGeometry
{
    private ResoniteImportedMesh mesh = null!;

    public ResoniteTriangleMeshGeometry(ResoniteImportedMesh Mesh)
    {
        this.Mesh = Mesh;
    }

    public ResoniteImportedMesh Mesh
    {
        get => mesh;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            mesh = value;
        }
    }

    public void Deconstruct(out ResoniteImportedMesh Mesh)
    {
        Mesh = this.Mesh;
    }
}

public sealed record ResoniteHeightMapGridGeometry : ResoniteConstructionGeometry
{
    private int width;
    private int height;
    private ResoniteFloat2 size = null!;
    private bool hasMinHeight;
    private bool hasMaxHeight;
    private bool hasHeightSamples;
    private double minHeight;
    private double maxHeight;
    private IReadOnlyList<double> heightSamples = Array.Empty<double>();

    public ResoniteHeightMapGridGeometry(
        int Width,
        int Height,
        ResoniteFloat2 Size,
        double MinHeight,
        double MaxHeight,
        IReadOnlyList<double> HeightSamples)
    {
        this.Width = Width;
        this.Height = Height;
        this.Size = Size;
        this.MaxHeight = MaxHeight;
        this.MinHeight = MinHeight;
        this.HeightSamples = CollectionCopy.List(HeightSamples, nameof(HeightSamples));

        if (this.HeightSamples.Count != Width * Height)
        {
            throw new ArgumentException("Height sample count must match width * height.", nameof(HeightSamples));
        }
    }

    public int Width
    {
        get => width;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            width = value;
            ValidateDimensions();
        }
    }

    public int Height
    {
        get => height;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            height = value;
            ValidateDimensions();
        }
    }

    public ResoniteFloat2 Size
    {
        get => size;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            size = value;
        }
    }

    public double MinHeight
    {
        get => minHeight;
        init
        {
            if (hasMaxHeight && value > maxHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(MinHeight), value, "MinHeight cannot exceed MaxHeight.");
            }

            minHeight = value;
            hasMinHeight = true;
        }
    }

    public double MaxHeight
    {
        get => maxHeight;
        init
        {
            if (hasMinHeight && minHeight > value)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxHeight), value, "MaxHeight cannot be less than MinHeight.");
            }

            maxHeight = value;
            hasMaxHeight = true;
        }
    }

    public IReadOnlyList<double> HeightSamples
    {
        get => heightSamples;
        init
        {
            heightSamples = CollectionCopy.List(value, nameof(HeightSamples));
            hasHeightSamples = true;
            ValidateDimensions();
        }
    }

    public void Deconstruct(
        out int Width,
        out int Height,
        out ResoniteFloat2 Size,
        out double MinHeight,
        out double MaxHeight,
        out IReadOnlyList<double> HeightSamples)
    {
        Width = this.Width;
        Height = this.Height;
        Size = this.Size;
        MinHeight = this.MinHeight;
        MaxHeight = this.MaxHeight;
        HeightSamples = this.HeightSamples;
    }

    private void ValidateDimensions()
    {
        if (width > 0
            && height > 0
            && hasHeightSamples
            && heightSamples.Count != width * height)
        {
            throw new ArgumentException("Height sample count must match width * height.", nameof(HeightSamples));
        }
    }

}

#pragma warning restore IDE0032
