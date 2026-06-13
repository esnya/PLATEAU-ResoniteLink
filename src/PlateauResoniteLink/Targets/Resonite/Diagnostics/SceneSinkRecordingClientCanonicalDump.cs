using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using PlateauResoniteLink.Application.Importing;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Diagnostics;

internal static class SceneSinkRecordingClientCanonicalDump
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string CreateCanonicalJson(
        SceneSinkRecordingClient client,
        IReadOnlyList<ImportedObjectUnit>? objectUnits = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        CanonicalDumpContext context = new(client);
        JsonObject root = new()
        {
            ["root"] = CreateSlotNode(context, "Root"),
            ["imports"] = CreateImportNode(client),
            ["emittedTerrainGrids"] = CreateEmittedTerrainGridNodes(client),
        };
        JsonArray objectNodes = CreateImportedObjectNode(objectUnits);
        if (objectNodes.Count > 0)
        {
            root["objects"] = objectNodes;
        }

        return root.ToJsonString(JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static JsonArray CreateImportedObjectNode(IReadOnlyList<ImportedObjectUnit>? objectUnits)
    {
        JsonArray objects = [];
        if (objectUnits is null)
        {
            return objects;
        }

        List<JsonObject> objectNodes = [];
        foreach (ImportedObjectUnit objectUnit in objectUnits)
        {
            foreach (ImportedCityObject cityObject in objectUnit.CityObjects)
            {
                JsonObject node = new()
                {
                    ["sourceFileRelativePath"] = objectUnit.Descriptor.SourceFileRelativePath,
                    ["sourceFileRootMeshCode"] = cityObject.SourceFileRootMeshCode,
                    ["matchedMeshCode"] = objectUnit.Descriptor.MatchedMeshCode,
                    ["objectKey"] = cityObject.ObjectKey,
                    ["displayName"] = cityObject.DisplayName,
                    ["packageName"] = cityObject.PackageName,
                    ["actualMeshCode"] = cityObject.ActualMeshCode,
                    ["geometryKind"] = GetGeometryKind(cityObject.Geometry),
                    ["transformPosition"] = CreateVector3Node(
                        cityObject.Transform.Position.X,
                        cityObject.Transform.Position.Y,
                        cityObject.Transform.Position.Z),
                };
                JsonObject? terrainGridSummary = cityObject.Geometry switch
                {
                    TerrainGridGeometry terrainGrid => CreateTerrainGridSummaryNode(cityObject.Transform, terrainGrid),
                    DynamicTerrainGeometry dynamicTerrain => CreateTerrainGridSummaryNode(cityObject.Transform, dynamicTerrain.GridMesh),
                    _ => null,
                };
                if (terrainGridSummary is not null)
                {
                    node["terrainGridSummary"] = terrainGridSummary;
                }

                JsonObject? triangleMeshVertexSummary = cityObject.Geometry switch
                {
                    TriangleMeshGeometry triangleMesh => CreateTriangleMeshWorldVertexSummaryNode(cityObject.Transform, triangleMesh.Mesh),
                    DynamicTerrainGeometry dynamicTerrain => CreateTriangleMeshWorldVertexSummaryNode(cityObject.Transform, dynamicTerrain.StaticMesh.Mesh),
                    _ => null,
                };
                if (triangleMeshVertexSummary is not null)
                {
                    node["triangleMeshWorldVertexSummary"] = triangleMeshVertexSummary;
                }

                JsonObject? terrainGridStaticMeshFootprintSummary = cityObject.Geometry switch
                {
                    DynamicTerrainGeometry dynamicTerrain => CreateTerrainGridStaticMeshFootprintSummaryNode(dynamicTerrain),
                    _ => null,
                };
                if (terrainGridStaticMeshFootprintSummary is not null)
                {
                    node["terrainGridStaticMeshFootprintSummary"] = terrainGridStaticMeshFootprintSummary;
                }

                objectNodes.Add(node);
            }
        }

        foreach (JsonObject objectNode in objectNodes.OrderBy(static node => CreateImportedObjectSortKey(node), StringComparer.Ordinal))
        {
            objects.Add(objectNode);
        }

        return objects;
    }

    private static JsonArray CreateEmittedTerrainGridNodes(SceneSinkRecordingClient client)
    {
        JsonArray nodes = [];
        List<JsonObject> gridNodes = [];
        foreach ((string componentId, Component component) in client.ComponentsById.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.GridMesh", StringComparison.Ordinal)
                || !TryCreateEmittedTerrainGridNode(client, componentId, component, out JsonObject? node)
                || node is null)
            {
                continue;
            }

            gridNodes.Add(node);
        }

        foreach (JsonObject gridNode in gridNodes.OrderBy(static node => (string?)node["slotPath"], StringComparer.Ordinal))
        {
            nodes.Add(gridNode);
        }

        return nodes;
    }

    private static bool TryCreateEmittedTerrainGridNode(
        SceneSinkRecordingClient client,
        string componentId,
        Component component,
        out JsonObject? node)
    {
        node = null;
        if (!TryGetGridMeshContainerSlot(client, componentId, out string? slotId)
            || slotId is null
            || !TryReadGridMeshMembers(component, out int2 points, out float2 size, out float displacementMagnitude, out floatQ rotation, out string? textureComponentId)
            || textureComponentId is null
            || !TryResolveHdrTexturePayload(client, textureComponentId, out RgbaFloat32RawTexturePayload? texture)
            || texture is null)
        {
            return false;
        }

        ResoniteFloat3 slotWorldPosition = ResolveSlotWorldPosition(client, slotId);
        WorldVertexSummary summary = new();
        WorldVertexSummary westEdgeSummary = new();
        WorldVertexSummary eastEdgeSummary = new();
        WorldVertexSummary southEdgeSummary = new();
        WorldVertexSummary northEdgeSummary = new();
        List<double> westEdgeHeights = [];
        List<double> eastEdgeHeights = [];
        List<double> southEdgeHeights = [];
        List<double> northEdgeHeights = [];
        int width = Math.Min(points.x, texture.Width);
        int height = Math.Min(points.y, texture.Height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double u = width == 1 ? 0.0 : (double)x / (width - 1);
                double v = height == 1 ? 0.0 : (double)y / (height - 1);
                double localX = (-size.x / 2.0) + (size.x * u);
                double localY = (-size.y / 2.0) + (size.y * v);
                double sampledDisplacement = ReadGridMeshDisplacement(texture, x, y) * displacementMagnitude;
                Float3 rotated = Rotate(new Float3(localX, localY, sampledDisplacement), rotation);
                double worldX = slotWorldPosition.X + rotated.X;
                double worldY = slotWorldPosition.Y + rotated.Y;
                double worldZ = slotWorldPosition.Z + rotated.Z;
                summary.Add(worldX, worldY, worldZ);
                if (x == 0)
                {
                    westEdgeSummary.Add(worldX, worldY, worldZ);
                    westEdgeHeights.Add(worldY);
                }

                if (x == width - 1)
                {
                    eastEdgeSummary.Add(worldX, worldY, worldZ);
                    eastEdgeHeights.Add(worldY);
                }

                if (y == 0)
                {
                    southEdgeSummary.Add(worldX, worldY, worldZ);
                    southEdgeHeights.Add(worldY);
                }

                if (y == height - 1)
                {
                    northEdgeSummary.Add(worldX, worldY, worldZ);
                    northEdgeHeights.Add(worldY);
                }
            }
        }

        string slotPath = client.SlotPaths.TryGetValue(slotId, out string? resolvedPath) ? resolvedPath : slotId;
        node = new JsonObject
        {
            ["componentId"] = componentId,
            ["slotPath"] = slotPath,
            ["slotWorldPosition"] = CreateVector3Node(slotWorldPosition.X, slotWorldPosition.Y, slotWorldPosition.Z),
            ["points"] = new JsonObject
            {
                ["x"] = points.x,
                ["y"] = points.y,
            },
            ["size"] = CreateVector2Node(size.x, size.y),
            ["displacementMagnitude"] = FormatNumber(displacementMagnitude),
            ["textureSize"] = new JsonObject
            {
                ["width"] = texture.Width,
                ["height"] = texture.Height,
            },
            ["worldVertexSummary"] = summary.CreateNode(),
            ["edgeWorldVertexSummaries"] = new JsonObject
            {
                ["west"] = CreateEmittedTerrainGridEdgeNode(westEdgeSummary, westEdgeHeights),
                ["east"] = CreateEmittedTerrainGridEdgeNode(eastEdgeSummary, eastEdgeHeights),
                ["south"] = CreateEmittedTerrainGridEdgeNode(southEdgeSummary, southEdgeHeights),
                ["north"] = CreateEmittedTerrainGridEdgeNode(northEdgeSummary, northEdgeHeights),
            },
        };
        return true;
    }

    private static JsonObject CreateEmittedTerrainGridEdgeNode(WorldVertexSummary summary, IReadOnlyList<double> worldHeights)
    {
        JsonArray samples = [];
        StringBuilder heightHashInput = new();
        foreach (double worldHeight in worldHeights)
        {
            string value = FormatNumber(worldHeight);
            samples.Add(value);
            heightHashInput
                .Append(Math.Round(worldHeight, 6, MidpointRounding.AwayFromZero).ToString("F6", CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return new JsonObject
        {
            ["summary"] = summary.CreateNode(),
            ["worldHeightSha256"] = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(heightHashInput.ToString()))).ToLowerInvariant(),
            ["worldHeightSamples"] = samples,
        };
    }

    private static string CreateImportedObjectSortKey(JsonObject node)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(string?)node["sourceFileRelativePath"]}|{(string?)node["packageName"]}|{(string?)node["actualMeshCode"]}|{(string?)node["objectKey"]}");
    }

    private static string GetGeometryKind(ConstructionGeometry geometry)
    {
        return geometry switch
        {
            TriangleMeshGeometry => "triangleMesh",
            TerrainGridGeometry => "terrainGrid",
            DynamicTerrainGeometry => "dynamicTerrain",
            _ => geometry.GetType().Name,
        };
    }

    private static JsonObject CreateTerrainGridSummaryNode(
        Transform3D transform,
        TerrainGridGeometry geometry)
    {
        int sampleCount = checked(geometry.Width * geometry.Height);
        if (geometry.HeightSamples.Count != sampleCount || geometry.SampleCoverage.Count != sampleCount)
        {
            return new JsonObject
            {
                ["width"] = geometry.Width,
                ["height"] = geometry.Height,
                ["sampleCount"] = sampleCount,
                ["heightSampleCount"] = geometry.HeightSamples.Count,
                ["coverageSampleCount"] = geometry.SampleCoverage.Count,
            };
        }

        TerrainGridCoverageSummary measured = new();
        TerrainGridCoverageSummary noSurface = new();
        int edgeSampleCount = 0;
        int edgeMeasuredSampleCount = 0;
        int edgeNoSurfaceSampleCount = 0;
        double baseHeight = transform.Position.Y;
        WorldVertexSummary gridVertexSummary = new();
        for (int y = 0; y < geometry.Height; y++)
        {
            for (int x = 0; x < geometry.Width; x++)
            {
                int sampleIndex = (y * geometry.Width) + x;
                double worldHeight = baseHeight + geometry.HeightSamples[sampleIndex];
                double u = geometry.Width == 1 ? 0.0 : (double)x / (geometry.Width - 1);
                double v = geometry.Height == 1 ? 0.0 : (double)y / (geometry.Height - 1);
                double worldX = (transform.Position.X - (geometry.Size.X / 2.0)) + (geometry.Size.X * u);
                double worldZ = (transform.Position.Z - (geometry.Size.Y / 2.0)) + (geometry.Size.Y * v);
                gridVertexSummary.Add(worldX, worldHeight, worldZ);
                TerrainGridSampleCoverage coverage = geometry.SampleCoverage[sampleIndex];
                bool edge = x == 0 || y == 0 || x == geometry.Width - 1 || y == geometry.Height - 1;
                if (edge)
                {
                    edgeSampleCount++;
                }

                if (coverage == TerrainGridSampleCoverage.Measured)
                {
                    measured.Add(worldHeight);
                    if (edge)
                    {
                        edgeMeasuredSampleCount++;
                    }
                }
                else
                {
                    noSurface.Add(worldHeight);
                    if (edge)
                    {
                        edgeNoSurfaceSampleCount++;
                    }
                }
            }
        }

        return new JsonObject
        {
            ["width"] = geometry.Width,
            ["height"] = geometry.Height,
            ["sampleCount"] = sampleCount,
            ["minHeight"] = FormatNumber(geometry.MinHeight),
            ["maxHeight"] = FormatNumber(geometry.MaxHeight),
            ["verticalOriginWorldHeight"] = FormatNumber(transform.Position.Y),
            ["displacementMagnitude"] = "-1",
            ["sampleWorldVertexSummary"] = gridVertexSummary.CreateNode(),
            ["measured"] = measured.CreateNode(),
            ["noSurface"] = noSurface.CreateNode(),
            ["edgeSampleCount"] = edgeSampleCount,
            ["edgeMeasuredSampleCount"] = edgeMeasuredSampleCount,
            ["edgeNoSurfaceSampleCount"] = edgeNoSurfaceSampleCount,
        };
    }

    private static JsonObject CreateTriangleMeshWorldVertexSummaryNode(
        Transform3D transform,
        ImportedMesh mesh)
    {
        WorldVertexSummary summary = new();
        foreach (MeshVertex vertex in mesh.Vertices)
        {
            summary.Add(
                transform.Position.X + vertex.Position.X,
                transform.Position.Y + vertex.Position.Y,
                transform.Position.Z + vertex.Position.Z);
        }

        return summary.CreateNode();
    }

    private static JsonObject CreateTerrainGridStaticMeshFootprintSummaryNode(DynamicTerrainGeometry geometry)
    {
        const double boundaryToleranceMeters = 0.25;
        TerrainGridGeometry grid = geometry.GridMesh;
        TerrainGridTriangle[] triangles = CreateTerrainGridTriangles(geometry.StaticMesh.Mesh);
        int noSurfaceSampleCount = 0;
        int noSurfaceWithinStaticMeshFootprintSampleCount = 0;
        int edgeNoSurfaceWithinStaticMeshFootprintSampleCount = 0;
        TerrainGridSpatialIndex spatialIndex = TerrainGridSpatialIndex.Create(
            triangles,
            -grid.Size.X / 2.0,
            grid.Size.X / 2.0,
            -grid.Size.Y / 2.0,
            grid.Size.Y / 2.0,
            boundaryToleranceMeters);

        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                int sampleIndex = (y * grid.Width) + x;
                if (grid.SampleCoverage[sampleIndex] != TerrainGridSampleCoverage.NoSurface)
                {
                    continue;
                }

                noSurfaceSampleCount++;
                double u = grid.Width == 1 ? 0.0 : (double)x / (grid.Width - 1);
                double v = grid.Height == 1 ? 0.0 : (double)y / (grid.Height - 1);
                double sampleX = (-grid.Size.X / 2.0) + (grid.Size.X * u);
                double sampleZ = (-grid.Size.Y / 2.0) + (grid.Size.Y * v);
                if (!CityGmlDemTerrainGridSampler.TrySampleLocalHeight(
                    sampleX,
                    sampleZ,
                    triangles,
                    spatialIndex,
                    out _))
                {
                    continue;
                }

                noSurfaceWithinStaticMeshFootprintSampleCount++;
                if (x == 0 || y == 0 || x == grid.Width - 1 || y == grid.Height - 1)
                {
                    edgeNoSurfaceWithinStaticMeshFootprintSampleCount++;
                }
            }
        }

        return new JsonObject
        {
            ["boundaryToleranceMeters"] = FormatNumber(boundaryToleranceMeters),
            ["staticMeshTriangleCount"] = triangles.Length,
            ["noSurfaceSampleCount"] = noSurfaceSampleCount,
            ["noSurfaceWithinStaticMeshFootprintSampleCount"] = noSurfaceWithinStaticMeshFootprintSampleCount,
            ["edgeNoSurfaceWithinStaticMeshFootprintSampleCount"] = edgeNoSurfaceWithinStaticMeshFootprintSampleCount,
        };
    }

    private static TerrainGridTriangle[] CreateTerrainGridTriangles(ImportedMesh mesh)
    {
        List<TerrainGridTriangle> triangles = [];
        foreach (MeshSubmesh submesh in mesh.Submeshes)
        {
            for (int index = 0; index + 2 < submesh.TriangleVertexIndices.Count; index += 3)
            {
                MeshVertex a = mesh.Vertices[submesh.TriangleVertexIndices[index]];
                MeshVertex b = mesh.Vertices[submesh.TriangleVertexIndices[index + 1]];
                MeshVertex c = mesh.Vertices[submesh.TriangleVertexIndices[index + 2]];
                triangles.Add(new TerrainGridTriangle(a.Position, b.Position, c.Position));
            }
        }

        return triangles.ToArray();
    }

    private static JsonObject CreateSlotNode(CanonicalDumpContext context, string slotId)
    {
        Slot slot = slotId == "Root"
            ? new Slot
            {
                ID = "Root",
                Name = new Field_string
                {
                    Value = "Root",
                },
            }
            : context.Client.SlotsById[slotId];
        string path = context.GetSlotPath(slotId);

        JsonObject node = new()
        {
            ["path"] = path,
            ["name"] = slot.Name?.Value ?? string.Empty,
        };
        AddOptionalMember(node, "position", slot.Position);
        AddOptionalMember(node, "rotation", slot.Rotation);
        AddOptionalMember(node, "tag", slot.Tag);

        JsonArray components = [];
        foreach (Component component in context.GetSlotComponents(slotId))
        {
            components.Add(CreateComponentNode(context, component));
        }

        if (components.Count > 0)
        {
            node["components"] = components;
        }

        JsonArray children = [];
        foreach (string childSlotId in context.GetChildSlotIds(slotId))
        {
            children.Add(CreateSlotNode(context, childSlotId));
        }

        if (children.Count > 0)
        {
            node["children"] = children;
        }

        return node;

        void AddOptionalMember(JsonObject target, string name, Member? member)
        {
            if (member is not null)
            {
                target[name] = CreateMemberNode(context, member);
            }
        }
    }

    private static JsonObject CreateComponentNode(CanonicalDumpContext context, Component component)
    {
        JsonObject node = new()
        {
            ["type"] = component.ComponentType ?? string.Empty,
        };

        JsonObject members = [];
        foreach ((string name, Member member) in component.Members.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            members[name] = CreateMemberNode(context, member);
        }

        if (members.Count > 0)
        {
            node["members"] = members;
        }

        return node;
    }

    private static bool TryGetGridMeshContainerSlot(
        SceneSinkRecordingClient client,
        string componentId,
        out string? slotId)
    {
        foreach ((string candidateSlotId, Slot slot) in client.SlotsById)
        {
            if (slot.Components is null)
            {
                continue;
            }

            foreach (Component component in slot.Components)
            {
                if (string.Equals(component.ID, componentId, StringComparison.Ordinal))
                {
                    slotId = candidateSlotId;
                    return true;
                }
            }
        }

        slotId = null;
        return false;
    }

    private static bool TryReadGridMeshMembers(
        Component component,
        out int2 points,
        out float2 size,
        out float displacementMagnitude,
        out floatQ rotation,
        out string? textureComponentId)
    {
        points = default;
        size = default;
        displacementMagnitude = 0.0f;
        rotation = default;
        textureComponentId = null;
        if (!component.Members.TryGetValue("Points", out Member? pointsMember)
            || pointsMember is not Field_int2 pointsField
            || !component.Members.TryGetValue("Size", out Member? sizeMember)
            || sizeMember is not Field_float2 sizeField
            || !component.Members.TryGetValue("DisplacementMagnitude", out Member? displacementMember)
            || displacementMember is not Field_float displacementField
            || !component.Members.TryGetValue("Rotation", out Member? rotationMember)
            || rotationMember is not Field_floatQ rotationField
            || !component.Members.TryGetValue("DisplacementTexture", out Member? textureMember)
            || textureMember is not Reference textureReference
            || string.IsNullOrWhiteSpace(textureReference.TargetID))
        {
            return false;
        }

        points = pointsField.Value;
        size = sizeField.Value;
        displacementMagnitude = displacementField.Value;
        rotation = rotationField.Value;
        textureComponentId = textureReference.TargetID;
        return true;
    }

    private static bool TryResolveHdrTexturePayload(
        SceneSinkRecordingClient client,
        string textureComponentId,
        out RgbaFloat32RawTexturePayload? texture)
    {
        texture = null;
        if (!client.ComponentsById.TryGetValue(textureComponentId, out Component? textureComponent)
            || !textureComponent.Members.TryGetValue("URL", out Member? urlMember)
            || urlMember is not Field_Uri url
            || url.Value is null)
        {
            return false;
        }

        string value = url.Value.ToString();
        if (!value.StartsWith("resdb:///texture/", StringComparison.Ordinal)
            || !int.TryParse(value["resdb:///texture/".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int textureIndex)
            || textureIndex < 0
            || textureIndex >= client.ImportedTexturePayloads.Count
            || client.ImportedTexturePayloads[textureIndex] is not RgbaFloat32RawTexturePayload hdrTexture)
        {
            return false;
        }

        texture = hdrTexture;
        return true;
    }

    private static ResoniteFloat3 ResolveSlotWorldPosition(SceneSinkRecordingClient client, string slotId)
    {
        double x = 0.0;
        double y = 0.0;
        double z = 0.0;
        HashSet<string> visited = new(StringComparer.Ordinal);
        string? currentSlotId = slotId;
        while (!string.IsNullOrWhiteSpace(currentSlotId)
            && visited.Add(currentSlotId)
            && client.SlotsById.TryGetValue(currentSlotId, out Slot? slot))
        {
            if (slot.Position is Field_float3 position)
            {
                x += position.Value.x;
                y += position.Value.y;
                z += position.Value.z;
            }

            currentSlotId = slot.Parent?.TargetID;
            if (string.Equals(currentSlotId, "Root", StringComparison.Ordinal))
            {
                break;
            }
        }

        return new ResoniteFloat3(x, y, z);
    }

    private static double ReadGridMeshDisplacement(RgbaFloat32RawTexturePayload texture, int x, int y)
    {
        int pixelIndex = (y * texture.Width) + x;
        int byteIndex = pixelIndex * 16;
        float r = BitConverter.ToSingle(texture.Bytes, byteIndex);
        float g = BitConverter.ToSingle(texture.Bytes, byteIndex + 4);
        float b = BitConverter.ToSingle(texture.Bytes, byteIndex + 8);
        return r + g + (b / 3.0f);
    }

    private static Float3 Rotate(Float3 value, floatQ rotation)
    {
        double qx = rotation.x;
        double qy = rotation.y;
        double qz = rotation.z;
        double qw = rotation.w;
        double tx = 2.0 * ((qy * value.Z) - (qz * value.Y));
        double ty = 2.0 * ((qz * value.X) - (qx * value.Z));
        double tz = 2.0 * ((qx * value.Y) - (qy * value.X));
        return new Float3(
            value.X + (qw * tx) + ((qy * tz) - (qz * ty)),
            value.Y + (qw * ty) + ((qz * tx) - (qx * tz)),
            value.Z + (qw * tz) + ((qx * ty) - (qy * tx)));
    }

    private static JsonObject CreateImportNode(SceneSinkRecordingClient client)
    {
        JsonArray meshes = [];
        List<JsonObject> meshNodes = [];
        for (int index = 0; index < client.ImportedMeshes.Count; index++)
        {
            ImportMeshRawData mesh = client.ImportedMeshes[index];
            meshNodes.Add(new JsonObject
            {
                ["uri"] = CreateMeshToken(mesh),
                ["vertexCount"] = mesh.VertexCount,
                ["hasNormals"] = mesh.HasNormals,
                ["hasTangents"] = mesh.HasTangents,
                ["hasColors"] = mesh.HasColors,
                ["submeshCount"] = mesh.Submeshes.Count,
                ["positionBounds"] = CreateMeshPositionBoundsNode(mesh),
                ["uvBounds"] = CreateMeshUvBoundsNode(mesh),
                ["payloadHash"] = HashBytes(mesh.RawBinaryPayload),
            });
        }

        foreach (JsonObject meshNode in meshNodes.OrderBy(static node => (string?)node["uri"], StringComparer.Ordinal))
        {
            meshes.Add(meshNode);
        }

        JsonArray textures = [];
        List<JsonObject> textureNodes = [];
        for (int index = 0; index < client.ImportedTexturePayloads.Count; index++)
        {
            if (client.ImportedTexturePayloads[index] is not Rgba32RawTexturePayload texture)
            {
                continue;
            }

            textureNodes.Add(new JsonObject
            {
                ["uri"] = CreateTextureToken(texture),
                ["width"] = texture.Width,
                ["height"] = texture.Height,
                ["colorProfile"] = texture.ColorProfile,
                ["payloadHash"] = HashBytes(texture.Bytes),
            });
        }

        foreach (JsonObject textureNode in textureNodes.OrderBy(static node => (string?)node["uri"], StringComparer.Ordinal))
        {
            textures.Add(textureNode);
        }

        JsonArray hdrTextures = [];
        List<JsonObject> hdrTextureNodes = [];
        for (int index = 0; index < client.ImportedTexturePayloads.Count; index++)
        {
            if (client.ImportedTexturePayloads[index] is not RgbaFloat32RawTexturePayload texture)
            {
                continue;
            }

            hdrTextureNodes.Add(new JsonObject
            {
                ["uri"] = CreateHdrTextureToken(texture),
                ["width"] = texture.Width,
                ["height"] = texture.Height,
                ["heightMapSummary"] = CreateHdrHeightMapSummaryNode(texture),
                ["payloadHash"] = HashBytes(texture.Bytes),
            });
        }

        foreach (JsonObject hdrTextureNode in hdrTextureNodes.OrderBy(static node => (string?)node["uri"], StringComparer.Ordinal))
        {
            hdrTextures.Add(hdrTextureNode);
        }

        return new JsonObject
        {
            ["meshes"] = meshes,
            ["textures"] = textures,
            ["hdrTextures"] = hdrTextures,
        };
    }

    private static JsonObject CreateMemberNode(CanonicalDumpContext context, Member member)
    {
        return member switch
        {
            Reference reference => CreateReferenceNode(context, reference),
            SyncList syncList => CreateSyncListNode(context, syncList),
            Field_Uri uri => CreateValueNode(
                "uri",
                uri.Value is null ? null : NormalizeUri(context.Client, uri.Value)),
            _ => CreateReflectiveMemberNode(context, member),
        };
    }

    private static JsonObject CreateReferenceNode(CanonicalDumpContext context, Reference reference)
    {
        return new JsonObject
        {
            ["kind"] = "reference",
            ["target"] = context.ResolveReference(reference.TargetID),
            ["targetType"] = string.IsNullOrWhiteSpace(reference.TargetType) ? null : reference.TargetType,
        };
    }

    private static JsonObject CreateSyncListNode(CanonicalDumpContext context, SyncList syncList)
    {
        JsonArray elements = [];
        foreach (object? element in syncList.Elements)
        {
            elements.Add(CreateObjectNode(context, element));
        }

        return new JsonObject
        {
            ["kind"] = "sync-list",
            ["elements"] = elements,
        };
    }

    private static JsonObject CreateReflectiveMemberNode(CanonicalDumpContext context, Member member)
    {
        PropertyInfo? boxedValueProperty = member.GetType().GetProperty("BoxedValue", BindingFlags.Public | BindingFlags.Instance);
        if (boxedValueProperty is not null)
        {
            return CreateValueNode(
                member.GetType().Name,
                CreateObjectNode(context, boxedValueProperty.GetValue(member)));
        }

        JsonObject properties = [];
        foreach (PropertyInfo property in member.GetType()
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(static property => property.GetIndexParameters().Length == 0)
                     .Where(static property => !string.Equals(property.Name, "ID", StringComparison.Ordinal))
                     .OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            properties[property.Name] = CreateObjectNode(context, property.GetValue(member));
        }

        return new JsonObject
        {
            ["kind"] = member.GetType().Name,
            ["properties"] = properties,
        };
    }

    private static JsonObject CreateValueNode(string kind, JsonNode? value)
    {
        return new JsonObject
        {
            ["kind"] = kind,
            ["value"] = value,
        };
    }

    private static JsonNode? CreateObjectNode(CanonicalDumpContext context, object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is Member member)
        {
            return CreateMemberNode(context, member);
        }

        if (value is Uri uri)
        {
            return NormalizeUri(context.Client, uri);
        }

        if (value is string stringValue)
        {
            return stringValue;
        }

        if (value is bool boolValue)
        {
            return boolValue;
        }

        if (value is int intValue)
        {
            return intValue;
        }

        if (value is long longValue)
        {
            return longValue;
        }

        if (value is float floatValue)
        {
            return FormatNumber(floatValue);
        }

        if (value is double doubleValue)
        {
            return FormatNumber(doubleValue);
        }

        if (value is System.Collections.IDictionary dictionary)
        {
            JsonObject dictionaryProperties = [];
            foreach (object? keyObject in dictionary.Keys
                         .Cast<object?>()
                         .OrderBy(static key => Convert.ToString(key, CultureInfo.InvariantCulture), StringComparer.Ordinal))
            {
                if (keyObject is null)
                {
                    continue;
                }

                string key = Convert.ToString(keyObject, CultureInfo.InvariantCulture) ?? string.Empty;
                dictionaryProperties[key] = CreateObjectNode(context, dictionary[keyObject]);
            }

            return dictionaryProperties;
        }

        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            JsonArray array = [];
            foreach (object? element in enumerable)
            {
                array.Add(CreateObjectNode(context, element));
            }

            return array;
        }

        Type type = value.GetType();
        if (type.IsPrimitive || type.IsEnum)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        JsonObject properties = [];
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(static property => property.GetIndexParameters().Length == 0)
                     .Where(static property => !string.Equals(property.Name, "ID", StringComparison.Ordinal))
                     .OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            properties[property.Name] = CreateObjectNode(context, property.GetValue(value));
        }

        if (properties.Count == 0)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        return properties;
    }

    private static JsonNode NormalizeUri(SceneSinkRecordingClient client, Uri uri)
    {
        string value = uri.ToString();
        if (value.StartsWith("resdb:///mesh/", StringComparison.Ordinal)
            && int.TryParse(value["resdb:///mesh/".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int meshIndex)
            && meshIndex >= 0
            && meshIndex < client.ImportedMeshes.Count)
        {
            return CreateMeshToken(client.ImportedMeshes[meshIndex]);
        }

        if (value.StartsWith("resdb:///texture/", StringComparison.Ordinal)
            && int.TryParse(value["resdb:///texture/".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int textureIndex))
        {
            if (textureIndex >= 0 && textureIndex < client.ImportedTexturePayloads.Count)
            {
                return CreateTextureToken(client.ImportedTexturePayloads[textureIndex]);
            }
        }

        return value;
    }

    private static string CreateMeshToken(ImportMeshRawData mesh)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"mesh:{mesh.VertexCount}:{mesh.Submeshes.Count}:{HashBytes(mesh.RawBinaryPayload)}");
    }

    private static string CreateTextureToken(RawTexturePayload texture)
    {
        return texture.Match(
            rgba32 => string.Create(
                CultureInfo.InvariantCulture,
                $"texture:{rgba32.Width}x{rgba32.Height}:{rgba32.ColorProfile}:{HashBytes(rgba32.Bytes)}"),
            rgbaFloat32 => string.Create(
                CultureInfo.InvariantCulture,
                $"hdr-texture:{rgbaFloat32.Width}x{rgbaFloat32.Height}:{HashBytes(rgbaFloat32.Bytes)}"));
    }

    private static string CreateHdrTextureToken(RgbaFloat32RawTexturePayload texture)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"hdr-texture:{texture.Width}x{texture.Height}:{HashBytes(texture.Bytes)}");
    }

    private static JsonObject CreateMeshPositionBoundsNode(ImportMeshRawData mesh)
    {
        if (mesh.VertexCount == 0)
        {
            return CreateEmptyBoundsNode();
        }

        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        double maxZ = double.NegativeInfinity;

        for (int index = 0; index < mesh.VertexCount; index++)
        {
            float3 position = mesh.Positions[index];
            minX = Math.Min(minX, position.x);
            minY = Math.Min(minY, position.y);
            minZ = Math.Min(minZ, position.z);
            maxX = Math.Max(maxX, position.x);
            maxY = Math.Max(maxY, position.y);
            maxZ = Math.Max(maxZ, position.z);
        }

        return new JsonObject
        {
            ["min"] = CreateVector3Node(minX, minY, minZ),
            ["max"] = CreateVector3Node(maxX, maxY, maxZ),
            ["size"] = CreateVector3Node(maxX - minX, maxY - minY, maxZ - minZ),
        };
    }

    private static JsonObject CreateMeshUvBoundsNode(ImportMeshRawData mesh)
    {
        if (mesh.VertexCount == 0 || mesh.UV_Channel_Dimensions.Count == 0)
        {
            return CreateEmptyBoundsNode();
        }

        Span<float2> uvChannel = mesh.AccessUV_2D(0);
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;

        for (int index = 0; index < mesh.VertexCount; index++)
        {
            float2 uv = uvChannel[index];
            minX = Math.Min(minX, uv.x);
            minY = Math.Min(minY, uv.y);
            maxX = Math.Max(maxX, uv.x);
            maxY = Math.Max(maxY, uv.y);
        }

        return new JsonObject
        {
            ["min"] = CreateVector2Node(minX, minY),
            ["max"] = CreateVector2Node(maxX, maxY),
            ["size"] = CreateVector2Node(maxX - minX, maxY - minY),
        };
    }

    private static JsonObject CreateHdrHeightMapSummaryNode(RgbaFloat32RawTexturePayload texture)
    {
        double minBlue = double.PositiveInfinity;
        double maxBlue = double.NegativeInfinity;
        double sumBlue = 0.0;
        int zeroLikeCount = 0;
        int maxLikeCount = 0;
        int edgeZeroLikeCount = 0;
        int edgeMaxLikeCount = 0;
        int edgePixelCount = 0;
        int pixelCount = checked(texture.Width * texture.Height);

        for (int y = 0; y < texture.Height; y++)
        {
            for (int x = 0; x < texture.Width; x++)
            {
                int pixelIndex = (y * texture.Width) + x;
                float blue = BitConverter.ToSingle(texture.Bytes, (pixelIndex * 16) + 8);
                minBlue = Math.Min(minBlue, blue);
                maxBlue = Math.Max(maxBlue, blue);
                sumBlue += blue;

                bool isZeroLike = Math.Abs(blue) <= 1e-6f;
                if (isZeroLike)
                {
                    zeroLikeCount++;
                }

                bool isMaxLike = Math.Abs(blue - 3.0f) <= 1e-6f;
                if (isMaxLike)
                {
                    maxLikeCount++;
                }

                if (x == 0 || y == 0 || x == texture.Width - 1 || y == texture.Height - 1)
                {
                    edgePixelCount++;
                    if (isZeroLike)
                    {
                        edgeZeroLikeCount++;
                    }

                    if (isMaxLike)
                    {
                        edgeMaxLikeCount++;
                    }
                }
            }
        }

        return new JsonObject
        {
            ["blueMin"] = FormatNumber(minBlue),
            ["blueMax"] = FormatNumber(maxBlue),
            ["blueAverage"] = FormatNumber(pixelCount == 0 ? 0.0 : sumBlue / pixelCount),
            ["zeroLikePixelCount"] = zeroLikeCount,
            ["maxLikePixelCount"] = maxLikeCount,
            ["edgePixelCount"] = edgePixelCount,
            ["edgeZeroLikePixelCount"] = edgeZeroLikeCount,
            ["edgeMaxLikePixelCount"] = edgeMaxLikeCount,
        };
    }

    private static JsonObject CreateEmptyBoundsNode()
    {
        return new JsonObject
        {
            ["min"] = null,
            ["max"] = null,
            ["size"] = null,
        };
    }

    private static JsonObject CreateVector3Node(double x, double y, double z)
    {
        return new JsonObject
        {
            ["x"] = FormatNumber(x),
            ["y"] = FormatNumber(y),
            ["z"] = FormatNumber(z),
        };
    }

    private static JsonObject CreateVector2Node(double x, double y)
    {
        return new JsonObject
        {
            ["x"] = FormatNumber(x),
            ["y"] = FormatNumber(y),
        };
    }

    private static string HashBytes(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return "empty";
        }

        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("G17", CultureInfo.InvariantCulture);
    }

    private sealed class CanonicalDumpContext
    {
        private readonly Dictionary<string, Slot[]> childrenByParentId;
        private readonly Dictionary<string, string> canonicalSlotPathsById;
        private readonly Dictionary<string, string> componentReferencesById;
        private readonly Dictionary<string, string> fieldReferencesById;

        public CanonicalDumpContext(SceneSinkRecordingClient client)
        {
            Client = client;
            childrenByParentId = CreateChildrenByParentId(client);
            canonicalSlotPathsById = CreateCanonicalSlotPaths(client, childrenByParentId);
            componentReferencesById = CreateComponentReferences(client);
            fieldReferencesById = CreateFieldReferences(client);
        }

        public SceneSinkRecordingClient Client { get; }

        public string GetSlotPath(string slotId)
        {
            return slotId == "Root"
                ? "Root"
                : GetSlotPath(Client, canonicalSlotPathsById, slotId);
        }

        public string[] GetChildSlotIds(string slotId)
        {
            return childrenByParentId.GetValueOrDefault(slotId, [])
                .OrderBy(slot => GetSlotPath(slot.ID ?? string.Empty), StringComparer.Ordinal)
                .Select(static slot => slot.ID!)
                .ToArray();
        }

        public Component[] GetSlotComponents(string slotId)
        {
            return Client.SlotsById.TryGetValue(slotId, out Slot? slot) && slot.Components is not null
                ? [.. slot.Components]
                : [];
        }

        public string ResolveReference(string? targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId))
            {
                return string.Empty;
            }

            if (Client.SlotsById.ContainsKey(targetId))
            {
                return $"slot:{GetSlotPath(targetId)}";
            }

            if (componentReferencesById.TryGetValue(targetId, out string? componentReference))
            {
                return componentReference;
            }

            if (fieldReferencesById.TryGetValue(targetId, out string? fieldReference))
            {
                return fieldReference;
            }

            return "external";
        }

        private static Dictionary<string, Slot[]> CreateChildrenByParentId(SceneSinkRecordingClient client)
        {
            return client.SlotsById.Values
                .Where(static slot => !string.IsNullOrWhiteSpace(slot.Parent?.TargetID))
                .GroupBy(static slot => slot.Parent!.TargetID!, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        }

        private static Dictionary<string, string> CreateCanonicalSlotPaths(
            SceneSinkRecordingClient client,
            IReadOnlyDictionary<string, Slot[]> childrenByParentId)
        {
            Dictionary<string, string> paths = new(StringComparer.Ordinal)
            {
                ["Root"] = "Root",
            };
            AddChildPaths("Root", string.Empty);
            return paths;

            void AddChildPaths(string parentId, string parentPath)
            {
                Slot[] children = childrenByParentId.GetValueOrDefault(parentId, [])
                    .OrderBy(static slot => slot.Name?.Value ?? string.Empty, StringComparer.Ordinal)
                    .ToArray();
                Dictionary<string, int> siblingNameCounts = children
                    .GroupBy(static slot => slot.Name?.Value ?? string.Empty, StringComparer.Ordinal)
                    .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
                Dictionary<string, int> siblingNameIndexes = new(StringComparer.Ordinal);
                foreach (Slot child in children)
                {
                    if (string.IsNullOrWhiteSpace(child.ID))
                    {
                        continue;
                    }

                    string slotName = child.Name?.Value ?? string.Empty;
                    int siblingIndex = siblingNameIndexes.GetValueOrDefault(slotName);
                    siblingNameIndexes[slotName] = siblingIndex + 1;
                    string escapedSlotName = EscapeSlotPathSegment(slotName);
                    string segment = siblingNameCounts[slotName] > 1
                        ? string.Create(CultureInfo.InvariantCulture, $"{escapedSlotName}#{siblingIndex}")
                        : escapedSlotName;
                    string childPath = string.IsNullOrEmpty(parentPath)
                        ? segment
                        : $"{parentPath}/{segment}";
                    paths[child.ID] = childPath;
                    AddChildPaths(child.ID, childPath);
                }
            }
        }

        private static string EscapeSlotPathSegment(string segment)
        {
            return segment
                .Replace("%", "%25", StringComparison.Ordinal)
                .Replace("\\", "%5C", StringComparison.Ordinal)
                .Replace("/", "%2F", StringComparison.Ordinal)
                .Replace("#", "%23", StringComparison.Ordinal);
        }

        private Dictionary<string, string> CreateComponentReferences(SceneSinkRecordingClient client)
        {
            Dictionary<string, string> references = new(StringComparer.Ordinal);
            foreach (Slot slot in client.SlotsById.Values.OrderBy(slot => GetSlotPath(slot.ID ?? string.Empty), StringComparer.Ordinal))
            {
                if (slot.Components is null)
                {
                    continue;
                }

                Dictionary<string, int> typeIndexes = new(StringComparer.Ordinal);
                foreach (Component component in slot.Components)
                {
                    string componentType = component.ComponentType ?? string.Empty;
                    int index = typeIndexes.GetValueOrDefault(componentType);
                    typeIndexes[componentType] = index + 1;
                    if (!string.IsNullOrWhiteSpace(component.ID))
                    {
                        references[component.ID] = $"component:{GetSlotPath(slot.ID ?? string.Empty)}:{componentType}#{index}";
                    }
                }
            }

            return references;
        }

        private Dictionary<string, string> CreateFieldReferences(SceneSinkRecordingClient client)
        {
            Dictionary<string, string> references = new(StringComparer.Ordinal);
            foreach (Slot slot in client.SlotsById.Values.OrderBy(slot => GetSlotPath(slot.ID ?? string.Empty), StringComparer.Ordinal))
            {
                string slotPath = GetSlotPath(slot.ID ?? string.Empty);
                AddFieldReference(references, slot.Name, $"field:slot:{slotPath}:Name");
                AddFieldReference(references, slot.Parent, $"field:slot:{slotPath}:Parent");
                AddFieldReference(references, slot.IsActive, $"field:slot:{slotPath}:IsActive");
                AddFieldReference(references, slot.OrderOffset, $"field:slot:{slotPath}:OrderOffset");
                AddFieldReference(references, slot.Position, $"field:slot:{slotPath}:Position");
                AddFieldReference(references, slot.Rotation, $"field:slot:{slotPath}:Rotation");
                AddFieldReference(references, slot.Tag, $"field:slot:{slotPath}:Tag");
            }

            foreach ((string fieldId, SyntheticSlotFieldReference syntheticReference) in client.SyntheticSlotFieldReferencesById)
            {
                if (!references.ContainsKey(fieldId))
                {
                    references[fieldId] = $"field:slot:{GetSlotPath(syntheticReference.SlotId)}:{syntheticReference.FieldName}";
                }
            }

            foreach ((string componentId, Component component) in client.ComponentsById.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                string componentReference = componentReferencesById.GetValueOrDefault(componentId, $"component:{componentId}");
                foreach ((string memberName, Member member) in component.Members.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    AddFieldReference(references, member, $"field:{componentReference}:{memberName}");
                }
            }

            return references;
        }

        private static void AddFieldReference(
            Dictionary<string, string> references,
            Member? member,
            string fieldReference)
        {
            if (member is null)
            {
                return;
            }

            if (TryGetMemberId(member) is { Length: > 0 } memberId)
            {
                references.TryAdd(memberId, fieldReference);
            }

            if (member is SyncList syncList)
            {
                for (int index = 0; index < syncList.Elements.Count; index++)
                {
                    if (syncList.Elements[index] is Member element)
                    {
                        AddFieldReference(
                            references,
                            element,
                            string.Create(CultureInfo.InvariantCulture, $"{fieldReference}[{index}]"));
                    }
                }
            }
        }

        private static string? TryGetMemberId(Member member)
        {
            return member.GetType().GetProperty("ID", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(member) as string;
        }

        private static string GetSlotPath(
            SceneSinkRecordingClient client,
            IReadOnlyDictionary<string, string> canonicalSlotPathsById,
            string slotId)
        {
            return canonicalSlotPathsById.GetValueOrDefault(
                slotId,
                client.SlotPaths.GetValueOrDefault(slotId, slotId).Replace('\\', '/'));
        }
    }

    private sealed class TerrainGridCoverageSummary
    {
        private double minWorldHeight = double.PositiveInfinity;
        private double maxWorldHeight = double.NegativeInfinity;
        private double sumWorldHeight;

        public int Count { get; private set; }

        public int NearSeaLevelCount { get; private set; }

        public int AboveOneMeterCount { get; private set; }

        public void Add(double worldHeight)
        {
            Count++;
            minWorldHeight = Math.Min(minWorldHeight, worldHeight);
            maxWorldHeight = Math.Max(maxWorldHeight, worldHeight);
            sumWorldHeight += worldHeight;
            if (Math.Abs(worldHeight) <= 0.1)
            {
                NearSeaLevelCount++;
            }

            if (worldHeight > 1.0)
            {
                AboveOneMeterCount++;
            }
        }

        public JsonObject CreateNode()
        {
            return new JsonObject
            {
                ["sampleCount"] = Count,
                ["nearSeaLevelSampleCount"] = NearSeaLevelCount,
                ["aboveOneMeterSampleCount"] = AboveOneMeterCount,
                ["worldHeightMin"] = Count == 0 ? null : FormatNumber(minWorldHeight),
                ["worldHeightMax"] = Count == 0 ? null : FormatNumber(maxWorldHeight),
                ["worldHeightAverage"] = Count == 0 ? null : FormatNumber(sumWorldHeight / Count),
            };
        }
    }

    private sealed class WorldVertexSummary
    {
        private readonly StringBuilder coordinates = new();
        private double minX = double.PositiveInfinity;
        private double maxX = double.NegativeInfinity;
        private double minY = double.PositiveInfinity;
        private double maxY = double.NegativeInfinity;
        private double minZ = double.PositiveInfinity;
        private double maxZ = double.NegativeInfinity;

        public int Count { get; private set; }

        public void Add(double x, double y, double z)
        {
            Count++;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
            minZ = Math.Min(minZ, z);
            maxZ = Math.Max(maxZ, z);
            coordinates
                .Append(FormatVertexHashNumber(x))
                .Append(',')
                .Append(FormatVertexHashNumber(y))
                .Append(',')
                .Append(FormatVertexHashNumber(z))
                .Append('\n');
        }

        public JsonObject CreateNode()
        {
            return new JsonObject
            {
                ["vertexCount"] = Count,
                ["worldVertexQuantizationMeters"] = "0.000001",
                ["worldVertexSha256"] = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(coordinates.ToString()))).ToLowerInvariant(),
                ["bounds"] = Count == 0
                    ? null
                    : new JsonObject
                    {
                        ["min"] = CreateVector3Node(minX, minY, minZ),
                        ["max"] = CreateVector3Node(maxX, maxY, maxZ),
                    },
            };
        }

        private static string FormatVertexHashNumber(double value)
        {
            return Math.Round(value, 6, MidpointRounding.AwayFromZero)
                .ToString("F6", CultureInfo.InvariantCulture);
        }
    }
}
