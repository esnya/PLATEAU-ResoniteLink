using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Targets.Resonite;

internal static class ResoniteMeshImportFactory
{
    public static ImportMeshRawData Create(ResoniteImportedMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        Validate(mesh);

        ResoniteMeshSubmesh[] orderedSubmeshes = mesh.Submeshes
            .OrderBy(static submesh => submesh.Index)
            .ToArray();

        ImportMeshRawData request = new()
        {
            VertexCount = mesh.Vertices.Count,
            HasNormals = true,
            HasTangents = false,
            HasColors = mesh.Vertices.Any(static vertex => vertex.Color is not null),
            BoneWeightCount = 0,
            UV_Channel_Dimensions = [2],
            Submeshes = orderedSubmeshes
                .Select(static submesh => (SubmeshRawData)new TriangleSubmeshRawData
                {
                    TriangleCount = submesh.TriangleVertexIndices.Count / 3,
                })
                .ToList(),
            Bones = [],
            BlendShapes = [],
        };

        request.AllocateBuffer();

        for (int index = 0; index < mesh.Vertices.Count; index++)
        {
            ResoniteMeshVertex vertex = mesh.Vertices[index];
            request.Positions[index] = new float3
            {
                x = (float)vertex.Position.X,
                y = (float)vertex.Position.Y,
                z = (float)vertex.Position.Z,
            };
            request.Normals[index] = new float3
            {
                x = (float)vertex.Normal.X,
                y = (float)vertex.Normal.Y,
                z = (float)vertex.Normal.Z,
            };
            request.AccessUV_2D(0)[index] = new float2
            {
                x = (float)vertex.UV0.X,
                y = (float)vertex.UV0.Y,
            };

            if (request.HasColors)
            {
                ResoniteColor color = vertex.Color ?? new ResoniteColor(1.0, 1.0, 1.0, 1.0);
                request.Colors[index] = new color
                {
                    r = (float)color.R,
                    g = (float)color.G,
                    b = (float)color.B,
                    a = (float)color.A,
                };
            }
        }

        for (int submeshIndex = 0; submeshIndex < orderedSubmeshes.Length; submeshIndex++)
        {
            TriangleSubmeshRawData rawSubmesh = (TriangleSubmeshRawData)request.Submeshes[submeshIndex];
            IReadOnlyList<int> indices = orderedSubmeshes[submeshIndex].TriangleVertexIndices;

            for (int index = 0; index < indices.Count; index++)
            {
                rawSubmesh.Indices[index] = indices[index];
            }
        }

        return request;
    }

    private static void Validate(ResoniteImportedMesh mesh)
    {
        if (mesh.Vertices.Count == 0)
        {
            throw new InvalidOperationException("Triangle mesh did not contain any vertex.");
        }

        if (mesh.Submeshes.Count == 0)
        {
            throw new InvalidOperationException("Triangle mesh did not contain any submesh.");
        }

        for (int vertexIndex = 0; vertexIndex < mesh.Vertices.Count; vertexIndex++)
        {
            ResoniteMeshVertex vertex = mesh.Vertices[vertexIndex];
            ThrowIfNotFinite(vertex.Position.X, "position.x", vertexIndex);
            ThrowIfNotFinite(vertex.Position.Y, "position.y", vertexIndex);
            ThrowIfNotFinite(vertex.Position.Z, "position.z", vertexIndex);
            ThrowIfNotFinite(vertex.Normal.X, "normal.x", vertexIndex);
            ThrowIfNotFinite(vertex.Normal.Y, "normal.y", vertexIndex);
            ThrowIfNotFinite(vertex.Normal.Z, "normal.z", vertexIndex);
            ThrowIfNotFinite(vertex.UV0.X, "uv0.x", vertexIndex);
            ThrowIfNotFinite(vertex.UV0.Y, "uv0.y", vertexIndex);

            if (vertex.Color is not null)
            {
                ThrowIfNotFinite(vertex.Color.R, "color.r", vertexIndex);
                ThrowIfNotFinite(vertex.Color.G, "color.g", vertexIndex);
                ThrowIfNotFinite(vertex.Color.B, "color.b", vertexIndex);
                ThrowIfNotFinite(vertex.Color.A, "color.a", vertexIndex);
            }
        }

        for (int submeshIndex = 0; submeshIndex < mesh.Submeshes.Count; submeshIndex++)
        {
            ResoniteMeshSubmesh submesh = mesh.Submeshes[submeshIndex];
            if (submesh.TriangleVertexIndices.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Triangle mesh submesh '{submesh.MaterialKey}' did not contain any index.");
            }

            if (submesh.TriangleVertexIndices.Count % 3 != 0)
            {
                throw new InvalidOperationException(
                    $"Triangle mesh submesh '{submesh.MaterialKey}' had {submesh.TriangleVertexIndices.Count} indices, which is not divisible by three.");
            }

            for (int triangleIndex = 0; triangleIndex < submesh.TriangleVertexIndices.Count; triangleIndex++)
            {
                int vertexIndex = submesh.TriangleVertexIndices[triangleIndex];
                if ((uint)vertexIndex >= (uint)mesh.Vertices.Count)
                {
                    throw new InvalidOperationException(
                        $"Triangle mesh submesh '{submesh.MaterialKey}' referenced vertex index {vertexIndex}, but vertex_count={mesh.Vertices.Count}.");
                }
            }
        }
    }

    private static void ThrowIfNotFinite(double value, string fieldName, int vertexIndex)
    {
        if (double.IsFinite(value))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Triangle mesh vertex {vertexIndex} contained non-finite {fieldName}={value}.");
    }
}
