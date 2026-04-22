using System.IO;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class SceneImportContractMapperTests
{
    [Fact]
    public void ToInternalMaterialBindingsPreservesNeutralContractFields()
    {
        MaterialBinding[] bindings =
        [
            new(
                MaterialKey: "shared",
                BaseColor: new ColorRgba(0.1, 0.2, 0.3, 0.4),
                MaterialType: MaterialType.Standard,
                TexturePayload: new TexturePayload(2, 2, "sRGB", [1, 2, 3, 4], "dataset:texture", TexturePayloadFormat.EncodedImage),
                TextureSourceKind: TextureSourceKind.Dataset,
                Projection: MaterialProjection.Uv,
                DepthOffset: new MaterialDepthOffset(-1.5, 2.5),
                SubmeshIndices: [0],
                TextureScale: new Float2(0.25, 0.5),
                Family: "roof",
                TextureOffset: new Float2(0.75, 0.125),
                ReuseScope: MaterialReuseScope.Shared,
                BundledVariantIndex: 3),
        ];

        ResoniteMaterialBinding mapped = Assert.Single(SceneImportContractMapper.ToInternal(bindings));

        Assert.Equal("shared", mapped.MaterialKey);
        Assert.Equal(0.1, mapped.BaseColor.R, 9);
        Assert.Equal(0.2, mapped.BaseColor.G, 9);
        Assert.Equal("dataset:texture", mapped.TexturePayload!.Identity);
        Assert.Equal(ResoniteTexturePayloadFormat.EncodedImage, mapped.TexturePayload.Format);
        Assert.Equal([1, 2, 3, 4], ReadAllBytes(mapped.TexturePayload.BinaryPayload));
        Assert.Equal(-1.5, mapped.DepthOffset!.Factor, 9);
        Assert.Equal(2.5, mapped.DepthOffset.Units, 9);
        Assert.Equal(0.25, mapped.TextureScale!.X, 9);
        Assert.Equal(0.125, mapped.TextureOffset!.Y, 9);
        Assert.Equal(ResoniteMaterialAssetScope.Common, mapped.AssetScope);
        Assert.Equal(3, mapped.BundledVariantIndex);
    }

    [Fact]
    public void ToContractCityObjectsPreservesTriangleMeshGeometry()
    {
        ResoniteConstructionCityObject[] cityObjects =
        [
            new(
                SlotKey: "slot",
                DisplayName: "City Object",
                PackageName: "bldg",
                ActualMeshCode: "53394525",
                LodLevel: 2,
                Transform: new ResoniteTransform(new ResoniteFloat3(1.0, 2.0, 3.0)),
                Geometry: new ResoniteTriangleMeshGeometry(
                    new ResoniteImportedMesh(
                        [
                            new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                            new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                            new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                        ],
                        [
                            new ResoniteMeshSubmesh(0, "shared", [0, 1, 2]),
                        ])),
                Materials:
                [
                    new ResoniteMaterialBinding(
                        "shared",
                        new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                        ResoniteMaterialType.Standard,
                        null,
                        ResoniteTextureSourceKind.Dataset,
                        ResoniteMaterialProjection.Uv,
                        null,
                        [0]),
                ],
                CollisionEnabled: true,
                SourceObjectKey: "object",
                SourceUnitKey: "unit",
                SourceFileRelativePath: "udx/bldg/53394525/sample.gml"),
        ];

        ImportedCityObject mapped = Assert.Single(SceneImportContractMapper.ToContract(cityObjects));
        TriangleMeshGeometry geometry = Assert.IsType<TriangleMeshGeometry>(mapped.Geometry);

        Assert.Equal("slot", mapped.ObjectKey);
        Assert.Equal("53394525", mapped.ActualMeshCode);
        Assert.Equal(1.0, mapped.Transform.Position.X, 9);
        Assert.Equal(3, geometry.Mesh.Vertices.Count);
        Assert.Equal("shared", Assert.Single(geometry.Mesh.Submeshes).MaterialKey);
        Assert.Equal("shared", Assert.Single(mapped.Materials).MaterialKey);
    }

    [Fact]
    public void ToContractMaterialBindingsExposesFreshReadablePayloadStream()
    {
        ResoniteMaterialBinding[] bindings =
        [
            new(
                MaterialKey: "shared",
                BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                MaterialType: ResoniteMaterialType.Standard,
                TexturePayload: new ResoniteTexturePayload(1, 1, "sRGB", [9, 8, 7, 6], "dataset:texture"),
                TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                Projection: ResoniteMaterialProjection.Uv,
                DepthOffset: null,
                SubmeshIndices: [0]),
        ];

        MaterialBinding mapped = Assert.Single(SceneImportContractMapper.ToContract(bindings));

        Assert.Equal([9, 8, 7, 6], ReadAllBytes(mapped.TexturePayload!.BinaryPayload));
        Assert.Equal([9, 8, 7, 6], ReadAllBytes(mapped.TexturePayload.BinaryPayload));
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using (stream)
        {
            using MemoryStream copy = new();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
    }
}
