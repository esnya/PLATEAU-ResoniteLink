#pragma warning disable IDE0032

namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteImportedMesh
{
    private bool hasVertices;
    private bool hasSubmeshes;
    private IReadOnlyList<ResoniteMeshVertex> vertices = Array.Empty<ResoniteMeshVertex>();
    private IReadOnlyList<ResoniteMeshSubmesh> submeshes = Array.Empty<ResoniteMeshSubmesh>();

    public ResoniteImportedMesh(
        IReadOnlyList<ResoniteMeshVertex> Vertices,
        IReadOnlyList<ResoniteMeshSubmesh> Submeshes)
    {
        this.Vertices = Vertices;
        this.Submeshes = Submeshes;
    }

    public IReadOnlyList<ResoniteMeshVertex> Vertices
    {
        get => vertices;
        init
        {
            vertices = CollectionCopy.List(value, nameof(Vertices));
            hasVertices = true;
            ValidateSubmeshIndices();
        }
    }

    public IReadOnlyList<ResoniteMeshSubmesh> Submeshes
    {
        get => submeshes;
        init
        {
            submeshes = CollectionCopy.List(value, nameof(Submeshes));
            hasSubmeshes = true;
            ValidateSubmeshIndices();
        }
    }

    public void Deconstruct(
        out IReadOnlyList<ResoniteMeshVertex> Vertices,
        out IReadOnlyList<ResoniteMeshSubmesh> Submeshes)
    {
        Vertices = this.Vertices;
        Submeshes = this.Submeshes;
    }

    private void ValidateSubmeshIndices()
    {
        if (!hasVertices || !hasSubmeshes)
        {
            return;
        }

        int maximumVertexIndex = vertices.Count - 1;
        int[] orderedSubmeshIndices = submeshes
            .Select(static submesh => submesh.Index)
            .OrderBy(static index => index)
            .ToArray();
        for (int expectedIndex = 0; expectedIndex < orderedSubmeshIndices.Length; expectedIndex++)
        {
            if (orderedSubmeshIndices[expectedIndex] != expectedIndex)
            {
                throw new ArgumentException(
                    "Submesh indices must be dense and zero-based.",
                    nameof(Submeshes));
            }
        }

        foreach (ResoniteMeshSubmesh submesh in submeshes)
        {
            foreach (int triangleVertexIndex in submesh.TriangleVertexIndices)
            {
                if (triangleVertexIndex > maximumVertexIndex)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(Submeshes),
                        triangleVertexIndex,
                        "Triangle indices must reference an existing vertex.");
                }
            }
        }
    }
}

#pragma warning restore IDE0032
