using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Targets.Resonite;

public abstract record ResoniteConstructionGeometry;

public sealed record ResoniteTriangleMeshGeometry(
    ResoniteImportedMesh Mesh)
    : ResoniteConstructionGeometry;

public sealed record ResoniteTerrainGridGeometry : ResoniteConstructionGeometry
{
    public ResoniteTerrainGridGeometry(
        int Width,
        int Height,
        ResoniteFloat2 Size,
        double MinHeight,
        double MaxHeight,
        IReadOnlyList<double> HeightSamples,
        ResoniteFloat2? UvScale = null,
        ResoniteFloat2? UvOffset = null)
    {
        ArgumentNullException.ThrowIfNull(Size);
        ArgumentNullException.ThrowIfNull(HeightSamples);

        if (Width < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), Width, "Terrain grid width must be at least 2.");
        }

        if (Height < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(Height), Height, "Terrain grid height must be at least 2.");
        }

        int sampleCount = checked(Width * Height);
        if (HeightSamples.Count != sampleCount)
        {
            throw new ArgumentException(
                $"Terrain grid height sample count {HeightSamples.Count} does not match sample count {sampleCount}.",
                nameof(HeightSamples));
        }

        this.Width = Width;
        this.Height = Height;
        this.Size = Size;
        this.MinHeight = MinHeight;
        this.MaxHeight = MaxHeight;
        this.HeightSamples = HeightSamples;
        this.UvScale = UvScale;
        this.UvOffset = UvOffset;
    }

    public int Width { get; init; }

    public int Height { get; init; }

    public ResoniteFloat2 Size { get; init; }

    public double MinHeight { get; init; }

    public double MaxHeight { get; init; }

    public IReadOnlyList<double> HeightSamples { get; init; }

    public ResoniteFloat2? UvScale { get; init; }

    public ResoniteFloat2? UvOffset { get; init; }

    public int SampleCount => checked(Width * Height);

    public double HeightRange => Math.Max(MaxHeight - MinHeight, 0.0);
}

public sealed record ResoniteDynamicTerrainGeometry(
    ResoniteTriangleMeshGeometry StaticMesh,
    ResoniteTerrainGridGeometry GridMesh)
    : ResoniteConstructionGeometry;
