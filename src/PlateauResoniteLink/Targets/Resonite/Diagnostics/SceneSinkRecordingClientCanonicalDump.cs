using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Diagnostics;

internal static class SceneSinkRecordingClientCanonicalDump
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string CreateCanonicalJson(SceneSinkRecordingClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        CanonicalDumpContext context = new(client);
        JsonObject root = new()
        {
            ["root"] = CreateSlotNode(context, "Root"),
            ["imports"] = CreateImportNode(client),
        };

        return root.ToJsonString(JsonOptions) + Environment.NewLine;
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
                ["payloadHash"] = HashBytes(mesh.RawBinaryPayload),
            });
        }

        foreach (JsonObject meshNode in meshNodes.OrderBy(static node => (string?)node["uri"], StringComparer.Ordinal))
        {
            meshes.Add(meshNode);
        }

        JsonArray textures = [];
        List<JsonObject> textureNodes = [];
        for (int index = 0; index < client.ImportedRawTextures.Count; index++)
        {
            ResoniteRawTextureImport texture = client.ImportedRawTextures[index];
            textureNodes.Add(new JsonObject
            {
                ["uri"] = CreateTextureToken(texture),
                ["width"] = texture.Width,
                ["height"] = texture.Height,
                ["colorProfile"] = texture.ColorProfile,
                ["payloadHash"] = HashBytes(texture.RawRgba32Bytes),
            });
        }

        foreach (JsonObject textureNode in textureNodes.OrderBy(static node => (string?)node["uri"], StringComparer.Ordinal))
        {
            textures.Add(textureNode);
        }

        JsonArray hdrTextures = [];
        List<JsonObject> hdrTextureNodes = [];
        for (int index = 0; index < client.ImportedRawHdrTextures.Count; index++)
        {
            ResoniteRawHdrTextureImport texture = client.ImportedRawHdrTextures[index];
            hdrTextureNodes.Add(new JsonObject
            {
                ["uri"] = CreateHdrTextureToken(texture),
                ["width"] = texture.Width,
                ["height"] = texture.Height,
                ["payloadHash"] = HashBytes(texture.RawRgbaFloatBytes),
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
            Field_Uri uri => CreateValueNode("uri", NormalizeUri(context.Client, uri.Value)),
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
            if (textureIndex >= 0 && textureIndex < client.ImportedTextures.Count)
            {
                return client.ImportedTextures[textureIndex] switch
                {
                    ResoniteRawTextureImport rawTexture => CreateTextureToken(rawTexture),
                    ResoniteRawHdrTextureImport hdrTexture => CreateHdrTextureToken(hdrTexture),
                    _ => value,
                };
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

    private static string CreateTextureToken(ResoniteRawTextureImport texture)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"texture:{texture.Width}x{texture.Height}:{texture.ColorProfile}:{HashBytes(texture.RawRgba32Bytes)}");
    }

    private static string CreateHdrTextureToken(ResoniteRawHdrTextureImport texture)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"hdr-texture:{texture.Width}x{texture.Height}:{HashBytes(texture.RawRgbaFloatBytes)}");
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
        private readonly Dictionary<string, string> canonicalSlotPathsById;
        private readonly Dictionary<string, string> componentReferencesById;

        public CanonicalDumpContext(SceneSinkRecordingClient client)
        {
            Client = client;
            canonicalSlotPathsById = CreateCanonicalSlotPaths(client);
            componentReferencesById = CreateComponentReferences(client);
        }

        public SceneSinkRecordingClient Client { get; }

        public string GetSlotPath(string slotId)
        {
            return slotId == "Root"
                ? "Root"
                : canonicalSlotPathsById.GetValueOrDefault(
                    slotId,
                    Client.SlotPaths.GetValueOrDefault(slotId, slotId).Replace('\\', '/'));
        }

        public string[] GetChildSlotIds(string slotId)
        {
            return Client.SlotsById.Values
                .Where(slot => string.Equals(slot.Parent?.TargetID, slotId, StringComparison.Ordinal))
                .OrderBy(slot => slot.Name?.Value ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(slot => GetSlotPath(slot.ID!), StringComparer.Ordinal)
                .Select(static slot => slot.ID!)
                .ToArray();
        }

        public Component[] GetSlotComponents(string slotId)
        {
            return Client.SlotsById.TryGetValue(slotId, out Slot? slot) && slot.Components is not null
                ? slot.Components
                    .OrderBy(static component => component.ComponentType, StringComparer.Ordinal)
                    .ThenBy(component => componentReferencesById.GetValueOrDefault(component.ID ?? string.Empty, string.Empty), StringComparer.Ordinal)
                    .ToArray()
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

            return $"external:{targetId}";
        }

        private static Dictionary<string, string> CreateCanonicalSlotPaths(SceneSinkRecordingClient client)
        {
            Dictionary<string, string> paths = new(StringComparer.Ordinal)
            {
                ["Root"] = "Root",
            };
            AddChildPaths("Root", string.Empty);
            return paths;

            void AddChildPaths(string parentId, string parentPath)
            {
                Slot[] children = client.SlotsById.Values
                    .Where(slot => string.Equals(slot.Parent?.TargetID, parentId, StringComparison.Ordinal))
                    .OrderBy(static slot => slot.Name?.Value ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(CreateSlotSemanticSortKey, StringComparer.Ordinal)
                    .ThenBy(slot => client.SlotPaths.GetValueOrDefault(slot.ID ?? string.Empty, string.Empty), StringComparer.Ordinal)
                    .ThenBy(static slot => slot.ID, StringComparer.Ordinal)
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
                    string segment = siblingNameCounts[slotName] > 1
                        ? string.Create(CultureInfo.InvariantCulture, $"{slotName}#{siblingIndex}")
                        : slotName;
                    string childPath = string.IsNullOrEmpty(parentPath)
                        ? segment.Replace('\\', '/')
                        : $"{parentPath}/{segment}".Replace('\\', '/');
                    paths[child.ID] = childPath;
                    AddChildPaths(child.ID, childPath);
                }
            }
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
                foreach (Component component in slot.Components
                             .OrderBy(static component => component.ComponentType, StringComparer.Ordinal)
                             .ThenBy(static component => component.ID, StringComparer.Ordinal))
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

        private static string CreateSlotSemanticSortKey(Slot slot)
        {
            string componentTypes = slot.Components is null
                ? string.Empty
                : string.Join(
                    "|",
                    slot.Components
                        .Select(static component => component.ComponentType ?? string.Empty)
                        .Order(StringComparer.Ordinal));
            string childNames = slot.Children is null
                ? string.Empty
                : string.Join(
                    "|",
                    slot.Children
                        .Select(static child => child.Name?.Value ?? string.Empty)
                        .Order(StringComparer.Ordinal));
            return string.Join(
                "\u001f",
                slot.Name?.Value ?? string.Empty,
                slot.Tag?.Value ?? string.Empty,
                componentTypes,
                childNames);
        }
    }
}
