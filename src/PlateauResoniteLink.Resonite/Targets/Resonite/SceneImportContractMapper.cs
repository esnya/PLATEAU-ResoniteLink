using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Core.Application.Importing.Contracts;


namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal static class SceneImportContractMapper
{
    internal static ResoniteMaterialBinding[] ToInternal(IReadOnlyList<MaterialBinding> bindings)
    {
        return ResoniteMaterialContractMapper.ToInternal(bindings);
    }

    internal static ResoniteConstructionCityObject ToInternal(ImportedCityObject cityObject)
    {
        return cityObject.Geometry switch
        {
            TriangleMeshGeometry triangleMesh => new ResoniteConstructionCityObject(
                cityObject.ObjectKey,
                cityObject.DisplayName,
                cityObject.PackageName,
                cityObject.ActualMeshCode,
                cityObject.LodLevel,
                ToInternal(cityObject.Transform),
                ToInternal(triangleMesh.Mesh),
                ResoniteMaterialContractMapper.ToInternal(cityObject.Materials),
                cityObject.CollisionEnabled,
                cityObject.SourceFileRelativePath,
                cityObject.SourceFileRootMeshCode,
                cityObject.Landmark,
                cityObject.DistanceCullingClass),
            TerrainGridGeometry heightMap => new ResoniteConstructionCityObject(
                cityObject.ObjectKey,
                cityObject.DisplayName,
                cityObject.PackageName,
                cityObject.ActualMeshCode,
                cityObject.LodLevel,
                ToInternal(cityObject.Transform),
                new ResoniteTerrainGridGeometry(
                    heightMap.Width,
                    heightMap.Height,
                    ToInternal(heightMap.Size),
                    heightMap.MinHeight,
                    heightMap.MaxHeight,
                    heightMap.HeightSamples,
                    heightMap.UvScale is null ? null : ToInternal(heightMap.UvScale),
                    heightMap.UvOffset is null ? null : ToInternal(heightMap.UvOffset)),
                ResoniteMaterialContractMapper.ToInternal(cityObject.Materials),
                cityObject.CollisionEnabled,
                cityObject.SourceFileRelativePath,
                cityObject.SourceFileRootMeshCode,
                cityObject.Landmark,
                cityObject.DistanceCullingClass),
            DynamicTerrainGeometry dynamicTerrain => new ResoniteConstructionCityObject(
                cityObject.ObjectKey,
                cityObject.DisplayName,
                cityObject.PackageName,
                cityObject.ActualMeshCode,
                cityObject.LodLevel,
                ToInternal(cityObject.Transform),
                new ResoniteDynamicTerrainGeometry(
                    new ResoniteTriangleMeshGeometry(ToInternal(dynamicTerrain.StaticMesh.Mesh)),
                    new ResoniteTerrainGridGeometry(
                        dynamicTerrain.GridMesh.Width,
                        dynamicTerrain.GridMesh.Height,
                        ToInternal(dynamicTerrain.GridMesh.Size),
                        dynamicTerrain.GridMesh.MinHeight,
                        dynamicTerrain.GridMesh.MaxHeight,
                        dynamicTerrain.GridMesh.HeightSamples,
                        dynamicTerrain.GridMesh.UvScale is null ? null : ToInternal(dynamicTerrain.GridMesh.UvScale),
                        dynamicTerrain.GridMesh.UvOffset is null ? null : ToInternal(dynamicTerrain.GridMesh.UvOffset))),
                ResoniteMaterialContractMapper.ToInternal(cityObject.Materials),
                cityObject.CollisionEnabled,
                cityObject.SourceFileRelativePath,
                cityObject.SourceFileRootMeshCode,
                cityObject.Landmark,
                cityObject.DistanceCullingClass),
            _ => throw new InvalidOperationException($"Unsupported geometry type '{cityObject.Geometry.GetType().Name}'."),
        };
    }

    private static ResoniteTransform ToInternal(Transform3D transform)
    {
        return new ResoniteTransform(
            ToInternal(transform.Position),
            transform.Rotation is null ? null : ToInternal(transform.Rotation));
    }

    private static ResoniteFloat2 ToInternal(Float2 value) => new(value.X, value.Y);

    private static ResoniteFloat3 ToInternal(Float3 value) => new(value.X, value.Y, value.Z);

    private static ResoniteFloatQ ToInternal(Quaternion value) => new(value.X, value.Y, value.Z, value.W);

    private static ResoniteColor ToInternal(ColorRgba value) => new(value.R, value.G, value.B, value.A);

    private static ResoniteImportedMesh ToInternal(ImportedMesh mesh)
    {
        return new ResoniteImportedMesh(
            mesh.Vertices.Select(ToInternal).ToArray(),
            mesh.Submeshes.Select(ToInternal).ToArray());
    }

    private static ResoniteMeshVertex ToInternal(MeshVertex vertex)
    {
        return new ResoniteMeshVertex(
            ToInternal(vertex.Position),
            ToInternal(vertex.Normal),
            ToInternal(vertex.UV0),
            vertex.Color is null ? null : ToInternal(vertex.Color));
    }

    private static ResoniteMeshSubmesh ToInternal(MeshSubmesh submesh)
    {
        return new ResoniteMeshSubmesh(submesh.Index, submesh.TriangleVertexIndices);
    }

    internal static ResoniteMaterialBinding ToInternal(MaterialBinding binding)
    {
        return ResoniteMaterialContractMapper.ToInternal(binding);
    }
}
