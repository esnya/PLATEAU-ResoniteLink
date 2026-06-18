using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Resonite.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal static class ResoniteMeshImportFactory
{
    // ResoniteLink raw mesh import uses Int32-backed vertex counts and index spans.
    internal const int MaxSupportedVertexCount = int.MaxValue;

    public static IGeometryImportSource Create(ResoniteImportedMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        Validate(mesh);
        return new ResoniteMeshImportSource(mesh);
    }

    private static ImportMeshRawData CreateRawData(ResoniteImportedMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

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
                request.Colors[index] = ResoniteColorSpace.CreateLinearVertexColor(color);
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

    private sealed class ResoniteMeshImportSource(ResoniteImportedMesh mesh) : IRawGeometryPayloadSource
    {
        public string Description => $"triangle-mesh:{VertexCount}:{SubmeshCount}";

        public int VertexCount => mesh.Vertices.Count;

        public int SubmeshCount => mesh.Submeshes.Count;

        public long? EstimatedByteLength => checked(
            (long)mesh.Vertices.Count * 128L
            + mesh.Submeshes.Sum(static submesh => (long)submesh.TriangleVertexIndices.Count * sizeof(int)));

        public ValueTask<ImportMeshRawData> MaterializeRawAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CreateRawData(mesh));
        }
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
            ThrowIfNotFloatRepresentable(vertex.Position.X, "position.x", vertexIndex);
            ThrowIfNotFloatRepresentable(vertex.Position.Y, "position.y", vertexIndex);
            ThrowIfNotFloatRepresentable(vertex.Position.Z, "position.z", vertexIndex);
            ThrowIfNotFinite(vertex.Normal.X, "normal.x", vertexIndex);
            ThrowIfNotFinite(vertex.Normal.Y, "normal.y", vertexIndex);
            ThrowIfNotFinite(vertex.Normal.Z, "normal.z", vertexIndex);
            ThrowIfNotFloatRepresentable(vertex.Normal.X, "normal.x", vertexIndex);
            ThrowIfNotFloatRepresentable(vertex.Normal.Y, "normal.y", vertexIndex);
            ThrowIfNotFloatRepresentable(vertex.Normal.Z, "normal.z", vertexIndex);
            ThrowIfNotFinite(vertex.UV0.X, "uv0.x", vertexIndex);
            ThrowIfNotFinite(vertex.UV0.Y, "uv0.y", vertexIndex);
            ThrowIfNotFloatRepresentable(vertex.UV0.X, "uv0.x", vertexIndex);
            ThrowIfNotFloatRepresentable(vertex.UV0.Y, "uv0.y", vertexIndex);

            double normalLengthSquared =
                (vertex.Normal.X * vertex.Normal.X)
                + (vertex.Normal.Y * vertex.Normal.Y)
                + (vertex.Normal.Z * vertex.Normal.Z);
            if (normalLengthSquared <= 1e-12)
            {
                throw new InvalidOperationException(
                    $"Triangle mesh vertex {vertexIndex} contained zero-length normal.");
            }

            if (vertex.Color is not null)
            {
                ThrowIfNotFinite(vertex.Color.R, "color.r", vertexIndex);
                ThrowIfNotFinite(vertex.Color.G, "color.g", vertexIndex);
                ThrowIfNotFinite(vertex.Color.B, "color.b", vertexIndex);
                ThrowIfNotFinite(vertex.Color.A, "color.a", vertexIndex);
                ThrowIfNotFloatRepresentable(vertex.Color.R, "color.r", vertexIndex);
                ThrowIfNotFloatRepresentable(vertex.Color.G, "color.g", vertexIndex);
                ThrowIfNotFloatRepresentable(vertex.Color.B, "color.b", vertexIndex);
                ThrowIfNotFloatRepresentable(vertex.Color.A, "color.a", vertexIndex);
            }
        }

        for (int submeshIndex = 0; submeshIndex < mesh.Submeshes.Count; submeshIndex++)
        {
            ResoniteMeshSubmesh submesh = mesh.Submeshes[submeshIndex];
            if (submesh.TriangleVertexIndices.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Triangle mesh submesh index {submesh.Index} did not contain any index.");
            }

            if (submesh.TriangleVertexIndices.Count % 3 != 0)
            {
                throw new InvalidOperationException(
                    $"Triangle mesh submesh index {submesh.Index} had {submesh.TriangleVertexIndices.Count} indices, which is not divisible by three.");
            }

            for (int triangleIndex = 0; triangleIndex < submesh.TriangleVertexIndices.Count; triangleIndex++)
            {
                int vertexIndex = submesh.TriangleVertexIndices[triangleIndex];
                if ((uint)vertexIndex >= (uint)mesh.Vertices.Count)
                {
                    throw new InvalidOperationException(
                        $"Triangle mesh submesh index {submesh.Index} referenced vertex index {vertexIndex}, but vertex_count={mesh.Vertices.Count}.");
                }
            }

            for (int triangleIndex = 0; triangleIndex < submesh.TriangleVertexIndices.Count; triangleIndex += 3)
            {
                ResoniteMeshVertex first = mesh.Vertices[submesh.TriangleVertexIndices[triangleIndex]];
                ResoniteMeshVertex second = mesh.Vertices[submesh.TriangleVertexIndices[triangleIndex + 1]];
                ResoniteMeshVertex third = mesh.Vertices[submesh.TriangleVertexIndices[triangleIndex + 2]];
                if (ComputeTriangleArea(first.Position, second.Position, third.Position) <= 1e-12)
                {
                    throw new InvalidOperationException(
                        $"Triangle mesh submesh index {submesh.Index} contained degenerate triangle at triangle_index={triangleIndex / 3}.");
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

    private static void ThrowIfNotFloatRepresentable(double value, string fieldName, int vertexIndex)
    {
        if (float.IsFinite((float)value))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Triangle mesh vertex {vertexIndex} contained {fieldName}={value}, which is not representable as float.");
    }

    private static double ComputeTriangleArea(ResoniteFloat3 first, ResoniteFloat3 second, ResoniteFloat3 third)
    {
        double ax = second.X - first.X;
        double ay = second.Y - first.Y;
        double az = second.Z - first.Z;
        double bx = third.X - first.X;
        double by = third.Y - first.Y;
        double bz = third.Z - first.Z;
        double crossX = (ay * bz) - (az * by);
        double crossY = (az * bx) - (ax * bz);
        double crossZ = (ax * by) - (ay * bx);
        return 0.5 * Math.Sqrt((crossX * crossX) + (crossY * crossY) + (crossZ * crossZ));
    }
}
