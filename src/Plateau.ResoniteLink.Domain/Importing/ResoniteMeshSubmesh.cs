#pragma warning disable IDE0032

namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteMeshSubmesh
{
    private string materialKey = string.Empty;
    private IReadOnlyList<int> triangleVertexIndices = Array.Empty<int>();

    public ResoniteMeshSubmesh(
        int Index,
        string MaterialKey,
        IReadOnlyList<int> TriangleVertexIndices)
    {
        this.Index = Index;
        this.MaterialKey = MaterialKey;
        this.TriangleVertexIndices = TriangleVertexIndices;
    }

    public int Index { get; init; }

    public string MaterialKey
    {
        get => materialKey;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            materialKey = value;
        }
    }

    public IReadOnlyList<int> TriangleVertexIndices
    {
        get => triangleVertexIndices;
        init
        {
            IReadOnlyList<int> copied = CollectionCopy.List(value, nameof(TriangleVertexIndices));
            if (copied.Count % 3 != 0)
            {
                throw new ArgumentException("Triangle index count must be a multiple of 3.", nameof(TriangleVertexIndices));
            }

            for (int index = 0; index < copied.Count; index++)
            {
                if (copied[index] < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(TriangleVertexIndices),
                        copied[index],
                        "Triangle indices cannot be negative.");
                }
            }

            triangleVertexIndices = copied;
        }
    }

    public void Deconstruct(
        out int Index,
        out string MaterialKey,
        out IReadOnlyList<int> TriangleVertexIndices)
    {
        Index = this.Index;
        MaterialKey = this.MaterialKey;
        TriangleVertexIndices = this.TriangleVertexIndices;
    }
}

#pragma warning restore IDE0032
