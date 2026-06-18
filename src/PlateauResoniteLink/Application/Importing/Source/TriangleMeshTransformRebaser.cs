using System.Linq;

using PlateauResoniteLink.Application.Importing.Contracts;

namespace PlateauResoniteLink.Application.Importing.Source;

internal static class TriangleMeshTransformRebaser
{
    internal static TriangleMeshGeometry Rebase(
        TriangleMeshGeometry source,
        Transform3D sourceTransform,
        Transform3D targetTransform)
    {
        ImportedMesh mesh = source.Mesh;
        MeshVertex[] vertices = mesh.Vertices
            .Select(vertex =>
            {
                Float3 worldPosition = TransformPointToWorld(sourceTransform, vertex.Position);
                Float3 localPosition = TransformVectorFromWorld(targetTransform, Subtract(worldPosition, targetTransform.Position));
                Float3 worldNormal = sourceTransform.Rotation is null ? vertex.Normal : Rotate(vertex.Normal, sourceTransform.Rotation);
                Float3 localNormal = TransformVectorFromWorld(targetTransform, worldNormal);
                return vertex with
                {
                    Position = localPosition,
                    Normal = localNormal,
                };
            })
            .ToArray();
        return new TriangleMeshGeometry(new ImportedMesh(vertices, mesh.Submeshes));
    }

    private static Float3 TransformPointToWorld(Transform3D transform, Float3 localPosition)
    {
        Float3 rotated = transform.Rotation is null
            ? localPosition
            : Rotate(localPosition, transform.Rotation);
        return Add(transform.Position, rotated);
    }

    private static Float3 TransformVectorFromWorld(Transform3D transform, Float3 worldVector)
    {
        return transform.Rotation is null
            ? worldVector
            : Rotate(worldVector, Conjugate(transform.Rotation));
    }

    private static Float3 Rotate(Float3 value, Quaternion rotation)
    {
        Float3 qv = new(rotation.X, rotation.Y, rotation.Z);
        Float3 uv = Cross(qv, value);
        Float3 uuv = Cross(qv, uv);
        return Add(
            value,
            Add(
                Scale(uv, 2.0 * rotation.W),
                Scale(uuv, 2.0)));
    }

    private static Quaternion Conjugate(Quaternion value)
    {
        return new Quaternion(-value.X, -value.Y, -value.Z, value.W);
    }

    private static Float3 Add(Float3 left, Float3 right)
    {
        return new Float3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    private static Float3 Subtract(Float3 left, Float3 right)
    {
        return new Float3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }

    private static Float3 Cross(Float3 left, Float3 right)
    {
        return new Float3(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
    }

    private static Float3 Scale(Float3 value, double scalar)
    {
        return new Float3(value.X * scalar, value.Y * scalar, value.Z * scalar);
    }
}
