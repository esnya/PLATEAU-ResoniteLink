namespace PlateauResoniteLink.Application.Importing;

internal static class ImportedCityObjectGeometryPredicates
{
    public static bool HasRenderableGeometry(ImportedCityObject cityObject)
    {
        return cityObject.Geometry switch
        {
            TriangleMeshGeometry triangleMesh => triangleMesh.Mesh.Submeshes.Count > 0,
            TerrainGridGeometry heightMap => heightMap.Width > 1 && heightMap.Height > 1,
            DynamicTerrainGeometry dynamicTerrain =>
                dynamicTerrain.StaticMesh.Mesh.Submeshes.Count > 0
                && dynamicTerrain.GridMesh.Width > 1
                && dynamicTerrain.GridMesh.Height > 1,
            _ => false,
        };
    }
}
