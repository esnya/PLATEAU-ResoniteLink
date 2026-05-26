using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using PlateauResoniteLink.Application.Importing;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

internal static class CanonicalSceneEquivalenceTestSupport
{
    public static string[] CreateSignature(
        string scenarioName,
        ImportExecutionResult result,
        SceneSinkRecordingClient client)
    {
        List<string> lines =
        [
            $"scenario={scenarioName}",
            $"scene={result.Metadata.SceneName}",
            $"processed={result.Destinations.Count}",
            $"source-files={string.Join("|", result.Metadata.SourceDataset.SourceFiles.Order(StringComparer.Ordinal))}",
            $"selected-meshes={string.Join("|", result.Metadata.SourceDataset.SelectedMeshCodes ?? [])}",
            $"data-usage={string.Join("|", (result.DataSourceUsages ?? []).Select(static usage => $"{usage.Category}:{usage.Identity}:{usage.UsedCount}").Order(StringComparer.Ordinal))}",
            $"connects={client.ConnectCallCount}",
            $"slots={client.SlotsById.Count}:{CreateSlotNameCounts(client)}",
            $"components={client.ComponentsById.Count}:{CreateComponentTypeCounts(client)}",
            $"batches={client.Batches.Count}:{CreateBatchSizeCounts(client)}",
            $"updates={client.UpdatedComponents.Count}",
            $"meshes={CreateMeshSignature(client.ImportedMeshes)}",
            $"textures={CreateTextureSignature(client)}",
            $"slot-gets={CreateSlotGetSignature(client)}",
        ];

        return lines.ToArray();
    }

    private static string CreateSlotNameCounts(SceneSinkRecordingClient client)
    {
        return string.Join(
            "|",
            client.SlotsById.Values
                .Select(static slot => slot.Name?.Value ?? "<unnamed>")
                .GroupBy(static name => name, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(static group => $"{group.Key}:{group.Count()}"));
    }

    private static string CreateComponentTypeCounts(SceneSinkRecordingClient client)
    {
        return string.Join(
            "|",
            client.ComponentsById.Values
                .Select(static component => component.ComponentType ?? "<null>")
                .GroupBy(static type => type, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(static group => $"{group.Key}:{group.Count()}"));
    }

    private static string CreateBatchSizeCounts(SceneSinkRecordingClient client)
    {
        return string.Join(
            "|",
            client.Batches
                .Select(static batch => batch.Count)
                .GroupBy(static count => count)
                .OrderBy(static group => group.Key)
                .Select(static group => $"{group.Key}:{group.Count()}"));
    }

    private static string CreateMeshSignature(IReadOnlyList<ImportMeshRawData> meshes)
    {
        return string.Join(
            "|",
            meshes.Select(CreateMeshEntrySignature)
                .GroupBy(static signature => signature, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(static group => $"{group.Key}:{group.Count()}"));
    }

    private static string CreateMeshEntrySignature(ImportMeshRawData mesh)
    {
        string submeshes = string.Join(
            "/",
            mesh.Submeshes
                .OfType<TriangleSubmeshRawData>()
                .Select(static submesh => submesh.TriangleCount.ToString(CultureInfo.InvariantCulture)));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{mesh.VertexCount}:{mesh.HasColors}:{submeshes}:{SumPositions(mesh):0.##}:{CreateUvCoverageSignature(mesh)}");
    }

    private static string CreateTextureSignature(SceneSinkRecordingClient client)
    {
        IEnumerable<string> raw = client.ImportedRawTextures.Select(static texture =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"raw:{texture.Width}x{texture.Height}:{texture.ColorProfile}:{texture.RawRgba32Bytes.Length}"));
        IEnumerable<string> hdr = client.ImportedRawHdrTextures.Select(static texture =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"hdr:{texture.Width}x{texture.Height}:{texture.RawRgbaFloatBytes.Length}"));
        return string.Join(
            "|",
            raw.Concat(hdr)
                .GroupBy(static signature => signature, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(static group => $"{group.Key}:{group.Count()}"));
    }

    private static string CreateSlotGetSignature(SceneSinkRecordingClient client)
    {
        return string.Join(
            "|",
            client.SlotGetRequests
                .Select(static request => $"{request.SlotPath}:{request.Depth}")
                .GroupBy(static signature => signature, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(static group => $"{group.Key}:{group.Count()}"));
    }

    private static double SumPositions(ImportMeshRawData mesh)
    {
        double total = 0.0;
        for (int index = 0; index < mesh.VertexCount; index++)
        {
            total += mesh.Positions[index].x;
            total += mesh.Positions[index].y;
            total += mesh.Positions[index].z;
        }

        return total;
    }

    private static string CreateUvCoverageSignature(ImportMeshRawData mesh)
    {
        Span<float2> uv0 = mesh.AccessUV_2D(0);
        if (uv0.Length == 0)
        {
            return "uv:0";
        }

        int finiteCount = 0;
        int nonZeroCount = 0;
        HashSet<double> distinctQuantizedUs = [];
        HashSet<double> distinctQuantizedVs = [];
        for (int index = 0; index < mesh.VertexCount; index++)
        {
            float2 uv = uv0[index];
            if (!float.IsFinite(uv.x) || !float.IsFinite(uv.y))
            {
                continue;
            }

            finiteCount++;
            if (MathF.Abs(uv.x) > 0.0001f || MathF.Abs(uv.y) > 0.0001f)
            {
                nonZeroCount++;
            }

            double quantizedU = Math.Round(uv.x, 2, MidpointRounding.AwayFromZero);
            double quantizedV = Math.Round(uv.y, 2, MidpointRounding.AwayFromZero);
            distinctQuantizedUs.Add(quantizedU);
            distinctQuantizedVs.Add(quantizedV);
        }

        return finiteCount == 0
            ? "uv:0"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"uv:{finiteCount}:{nonZeroCount}:{distinctQuantizedUs.Count}/{distinctQuantizedVs.Count}");
    }
}
