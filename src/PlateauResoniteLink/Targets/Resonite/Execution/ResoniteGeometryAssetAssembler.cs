using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal interface IResoniteGeometryAssetAssembler
{
    Task<PreparedGeometryAssetBatch> PrepareTriangleMeshAsync(
        IResoniteLinkClient importClient,
        string meshAssetSlotName,
        string displayName,
        ImportMeshRawData meshImport,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);

    Task<PreparedGeometryAssetBatch> PrepareHeightMapGridAsync(
        IResoniteLinkClient importClient,
        string meshAssetSlotName,
        string heightMapAssetSlotName,
        string displayName,
        ResoniteHeightMapGridGeometry geometry,
        ResoniteRawHdrTextureImport heightTextureImport,
        ResoniteFloat2? uvScale,
        ResoniteFloat2? uvOffset,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteGeometryAssetAssembler : IResoniteGeometryAssetAssembler
{
    private const string HeightMapSkirtAssetSlotSuffix = "_skirt";

    public async Task<PreparedGeometryAssetBatch> PrepareTriangleMeshAsync(
        IResoniteLinkClient importClient,
        string meshAssetSlotName,
        string displayName,
        ImportMeshRawData meshImport,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        return await PrepareTriangleMeshCoreAsync(
            importClient,
            meshAssetSlotName,
            displayName,
            meshImport,
            progressReporter,
            cancellationToken);
    }

    public async Task<PreparedGeometryAssetBatch> PrepareHeightMapGridAsync(
        IResoniteLinkClient importClient,
        string meshAssetSlotName,
        string heightMapAssetSlotName,
        string displayName,
        ResoniteHeightMapGridGeometry geometry,
        ResoniteRawHdrTextureImport heightTextureImport,
        ResoniteFloat2? uvScale,
        ResoniteFloat2? uvOffset,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(importClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshAssetSlotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(heightMapAssetSlotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(heightTextureImport);

        progressReporter?.Invoke(
            $"[live] HeightMap '{geometry.Width}x{geometry.Height}' importing displacement texture via raw payload.");
        Uri textureUri = await importClient.ImportTextureAsync(heightTextureImport, cancellationToken);
        progressReporter?.Invoke($"[live] HeightMap '{displayName}' displacement texture import completed -> '{textureUri}'.");

        PreparedTriangleMeshAssetBatch[] visualFallbackAssets = await PrepareBorderSkirtFallbackAssetsAsync(
            importClient,
            heightMapAssetSlotName,
            displayName,
            geometry,
            uvScale,
            uvOffset,
            progressReporter,
            cancellationToken);

        return new PreparedHeightMapGridAssetBatch(
            meshAssetSlotName,
            heightMapAssetSlotName,
            geometry,
            textureUri,
            uvScale,
            uvOffset,
            visualFallbackAssets);
    }

    private static async Task<PreparedTriangleMeshAssetBatch> PrepareTriangleMeshCoreAsync(
        IResoniteLinkClient importClient,
        string meshAssetSlotName,
        string displayName,
        ImportMeshRawData meshImport,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        progressReporter?.Invoke(
            $"[live] Mesh '{displayName}' importing triangle mesh "
            + $"(vertices={meshImport.VertexCount}, submeshes={meshImport.Submeshes.Count}).");
        Uri assetUri = await importClient.ImportMeshAsync(meshImport, cancellationToken);
        progressReporter?.Invoke($"[live] Mesh '{displayName}' mesh import completed -> '{assetUri}'.");
        return new PreparedTriangleMeshAssetBatch(meshAssetSlotName, assetUri);
    }

    private static async Task<PreparedTriangleMeshAssetBatch[]> PrepareBorderSkirtFallbackAssetsAsync(
        IResoniteLinkClient importClient,
        string heightMapAssetSlotName,
        string displayName,
        ResoniteHeightMapGridGeometry geometry,
        ResoniteFloat2? uvScale,
        ResoniteFloat2? uvOffset,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ResoniteImportedMesh? skirtMesh = TryCreateHeightMapBorderSkirtMesh(geometry, uvScale, uvOffset);
        if (skirtMesh is null)
        {
            return [];
        }

        ImportMeshRawData skirtMeshImport = ResoniteMeshImportFactory.Create(skirtMesh);
        PreparedTriangleMeshAssetBatch preparedSkirtAsset = await PrepareTriangleMeshCoreAsync(
            importClient,
            string.Concat(heightMapAssetSlotName, HeightMapSkirtAssetSlotSuffix),
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{displayName} Border Skirt"),
            skirtMeshImport,
            progressReporter,
            cancellationToken);
        return [preparedSkirtAsset];
    }

    private static ResoniteImportedMesh? TryCreateHeightMapBorderSkirtMesh(
        ResoniteHeightMapGridGeometry geometry,
        ResoniteFloat2? uvScale,
        ResoniteFloat2? uvOffset)
    {
        if (geometry.Width < 2 || geometry.Height < 2)
        {
            return null;
        }

        double skirtDepthMeters = ResolveHeightMapBorderSkirtDepthMeters(geometry);
        List<ResoniteMeshVertex> vertices = [];
        List<int> indices = [];
        AppendHeightMapBorderSkirtHorizontalEdge(geometry, row: 0, outwardY: -1.0, skirtDepthMeters, uvScale, uvOffset, vertices, indices);
        AppendHeightMapBorderSkirtHorizontalEdge(geometry, row: geometry.Height - 1, outwardY: 1.0, skirtDepthMeters, uvScale, uvOffset, vertices, indices);
        AppendHeightMapBorderSkirtVerticalEdge(geometry, column: 0, outwardX: -1.0, skirtDepthMeters, uvScale, uvOffset, vertices, indices);
        AppendHeightMapBorderSkirtVerticalEdge(geometry, column: geometry.Width - 1, outwardX: 1.0, skirtDepthMeters, uvScale, uvOffset, vertices, indices);

        return vertices.Count == 0
            ? null
            : new ResoniteImportedMesh(
                vertices,
                [new ResoniteMeshSubmesh(0, "heightmap-border-skirt", indices)]);
    }

    private static void AppendHeightMapBorderSkirtHorizontalEdge(
        ResoniteHeightMapGridGeometry geometry,
        int row,
        double outwardY,
        double skirtDepthMeters,
        ResoniteFloat2? uvScale,
        ResoniteFloat2? uvOffset,
        List<ResoniteMeshVertex> vertices,
        List<int> indices)
    {
        for (int column = 0; column < geometry.Width - 1; column++)
        {
            ResoniteMeshVertex topLeft = CreateHeightMapBorderSkirtVertex(geometry, column, row, uvScale, uvOffset);
            ResoniteMeshVertex topRight = CreateHeightMapBorderSkirtVertex(geometry, column + 1, row, uvScale, uvOffset);
            ResoniteMeshVertex bottomLeft = topLeft with
            {
                Position = new ResoniteFloat3(topLeft.Position.X, topLeft.Position.Y, topLeft.Position.Z - skirtDepthMeters),
                Normal = new ResoniteFloat3(0.0, outwardY, 0.0),
            };
            ResoniteMeshVertex bottomRight = topRight with
            {
                Position = new ResoniteFloat3(topRight.Position.X, topRight.Position.Y, topRight.Position.Z - skirtDepthMeters),
                Normal = new ResoniteFloat3(0.0, outwardY, 0.0),
            };

            topLeft = topLeft with { Normal = new ResoniteFloat3(0.0, outwardY, 0.0) };
            topRight = topRight with { Normal = new ResoniteFloat3(0.0, outwardY, 0.0) };
            AppendQuad(vertices, indices, topLeft, topRight, bottomLeft, bottomRight, outwardY < 0.0);
        }
    }

    private static void AppendHeightMapBorderSkirtVerticalEdge(
        ResoniteHeightMapGridGeometry geometry,
        int column,
        double outwardX,
        double skirtDepthMeters,
        ResoniteFloat2? uvScale,
        ResoniteFloat2? uvOffset,
        List<ResoniteMeshVertex> vertices,
        List<int> indices)
    {
        for (int row = 0; row < geometry.Height - 1; row++)
        {
            ResoniteMeshVertex topNear = CreateHeightMapBorderSkirtVertex(geometry, column, row, uvScale, uvOffset);
            ResoniteMeshVertex topFar = CreateHeightMapBorderSkirtVertex(geometry, column, row + 1, uvScale, uvOffset);
            ResoniteMeshVertex bottomNear = topNear with
            {
                Position = new ResoniteFloat3(topNear.Position.X, topNear.Position.Y, topNear.Position.Z - skirtDepthMeters),
                Normal = new ResoniteFloat3(outwardX, 0.0, 0.0),
            };
            ResoniteMeshVertex bottomFar = topFar with
            {
                Position = new ResoniteFloat3(topFar.Position.X, topFar.Position.Y, topFar.Position.Z - skirtDepthMeters),
                Normal = new ResoniteFloat3(outwardX, 0.0, 0.0),
            };

            topNear = topNear with { Normal = new ResoniteFloat3(outwardX, 0.0, 0.0) };
            topFar = topFar with { Normal = new ResoniteFloat3(outwardX, 0.0, 0.0) };
            AppendQuad(vertices, indices, topNear, topFar, bottomNear, bottomFar, outwardX < 0.0);
        }
    }

    private static ResoniteMeshVertex CreateHeightMapBorderSkirtVertex(
        ResoniteHeightMapGridGeometry geometry,
        int column,
        int row,
        ResoniteFloat2? uvScale,
        ResoniteFloat2? uvOffset)
    {
        double x = geometry.Width == 1
            ? 0.0
            : (-geometry.Size.X / 2.0) + (geometry.Size.X * column / (geometry.Width - 1.0));
        double y = geometry.Height == 1
            ? 0.0
            : (-geometry.Size.Y / 2.0) + (geometry.Size.Y * row / (geometry.Height - 1.0));
        double height = geometry.HeightSamples[(row * geometry.Width) + column] - geometry.MaxHeight;
        double baseU = geometry.Width == 1 ? 0.0 : column / (geometry.Width - 1.0);
        double baseV = geometry.Height == 1 ? 0.0 : row / (geometry.Height - 1.0);

        return new ResoniteMeshVertex(
            new ResoniteFloat3(x, y, height),
            new ResoniteFloat3(0.0, 0.0, 1.0),
            new ResoniteFloat2(
                (baseU * (uvScale?.X ?? 1.0)) + (uvOffset?.X ?? 0.0),
                (baseV * (uvScale?.Y ?? 1.0)) + (uvOffset?.Y ?? 0.0)));
    }

    private static double ResolveHeightMapBorderSkirtDepthMeters(ResoniteHeightMapGridGeometry geometry)
    {
        return Math.Max(1.0, geometry.MaxHeight - geometry.MinHeight);
    }

    private static void AppendQuad(
        List<ResoniteMeshVertex> vertices,
        List<int> indices,
        ResoniteMeshVertex topLeft,
        ResoniteMeshVertex topRight,
        ResoniteMeshVertex bottomLeft,
        ResoniteMeshVertex bottomRight,
        bool flipWinding)
    {
        int baseIndex = vertices.Count;
        vertices.Add(topLeft);
        vertices.Add(topRight);
        vertices.Add(bottomLeft);
        vertices.Add(bottomRight);

        if (!flipWinding)
        {
            indices.AddRange([baseIndex, baseIndex + 2, baseIndex + 1, baseIndex + 1, baseIndex + 2, baseIndex + 3]);
            return;
        }

        indices.AddRange([baseIndex, baseIndex + 1, baseIndex + 2, baseIndex + 1, baseIndex + 3, baseIndex + 2]);
    }

    internal static Dictionary<string, Member> CreateHeightMapTextureMembers(Uri assetUri)
    {
        return new Dictionary<string, Member>(StringComparer.Ordinal)
        {
            ["Readable"] = new Field_bool { Value = true },
            ["Uncompressed"] = new Field_bool { Value = true },
            ["DirectLoad"] = new Field_bool { Value = true },
            ["MipMaps"] = new Field_bool { Value = false },
            ["URL"] = new Field_Uri
            {
                Value = assetUri,
            },
            ["WrapModeU"] = new Field_Enum { Value = "Clamp" },
            ["WrapModeV"] = new Field_Enum { Value = "Clamp" },
            ["FilterMode"] = new Field_Nullable_Enum { Value = "Point" },
        };
    }
}

internal abstract record PreparedGeometryAssetBatch(string MeshAssetSlotName);

internal sealed record PreparedTriangleMeshAssetBatch(
    string MeshAssetSlotName,
    Uri MeshUri) : PreparedGeometryAssetBatch(MeshAssetSlotName);

internal sealed record PreparedHeightMapGridAssetBatch(
    string MeshAssetSlotName,
    string HeightMapAssetSlotName,
    ResoniteHeightMapGridGeometry Geometry,
    Uri HeightTextureUri,
    ResoniteFloat2? UvScale,
    ResoniteFloat2? UvOffset,
    IReadOnlyList<PreparedTriangleMeshAssetBatch>? VisualFallbackAssets = null) : PreparedGeometryAssetBatch(MeshAssetSlotName);
