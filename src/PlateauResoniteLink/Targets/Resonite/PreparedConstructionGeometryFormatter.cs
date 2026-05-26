namespace PlateauResoniteLink.Targets.Resonite;

internal static class PreparedConstructionGeometryFormatter
{
    public static string Describe(PreparedConstructionGeometry geometry)
    {
        return geometry switch
        {
            PreparedTriangleMeshGeometry triangleMesh =>
                $"triangle-mesh(vertices={triangleMesh.MeshImport.VertexCount}, submeshes={triangleMesh.MeshImport.Submeshes.Count})",
            PreparedTerrainGridGeometry heightMap =>
                $"terrain-grid({heightMap.Geometry.Width}x{heightMap.Geometry.Height})",
            PreparedDynamicTerrainGeometry dynamicTerrain =>
                $"dynamic-terrain(static={dynamicTerrain.StaticMesh.MeshImport.VertexCount} vertices, grid={dynamicTerrain.GridMesh.Geometry.Width}x{dynamicTerrain.GridMesh.Geometry.Height})",
            _ => geometry.GetType().Name,
        };
    }
}
