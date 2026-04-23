using System;
using System.Collections.Generic;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class NonDemBatchingTests
{
    private static readonly NonDemBatchMaterialPolicy DefaultPolicy = new(
        Name: "default",
        RequireAtlasCandidateMaterial: true,
        PreserveVertexColorMaterials: true,
        PreserveTexturelessMaterials: true,
        PreserveSharedMaterials: true);

    [Fact]
    public void ResolveRequiredScopeKeyUsesSourceFileRelativePathBeforeSourceUnitKey()
    {
        ImportedCityObject cityObject = CreateCityObject(
            sourceUnitKey: "source-unit",
            sourceFileRelativePath: "udx/bldg/53394525/building.gml");

        ImportedCityObjectScopeKey scopeKey = NonDemBatching.ResolveRequiredScopeKey(cityObject);

        Assert.Equal("udx/bldg/53394525/building.gml", scopeKey.CityGmlScopeKey);
        Assert.Equal(2, scopeKey.LodLevel);
    }

    [Fact]
    public void ResolveRequiredScopeKeyThrowsWhenSourceScopeIsMissing()
    {
        ImportedCityObject cityObject = CreateCityObject(
            sourceUnitKey: null,
            sourceFileRelativePath: null);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => NonDemBatching.ResolveRequiredScopeKey(cityObject));

        Assert.Contains("SourceFileRelativePath or SourceUnitKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSourceUnitBatchKeyUsesImportedCityObjectIdentityFields()
    {
        ImportedCityObject cityObject = CreateCityObject(
            objectKey: "building-01",
            sourceUnitKey: "source-unit",
            sourceFileRelativePath: "udx/bldg/53394525/building.gml");

        NonDemSourceUnitBatchKey batchKey = NonDemBatching.CreateSourceUnitBatchKey(cityObject, DefaultPolicy);

        Assert.Equal("53394525", batchKey.ActualMeshCode);
        Assert.Equal("bldg", batchKey.PackageName);
        Assert.Equal(2, batchKey.LodLevel);
        Assert.Equal("default", batchKey.PolicyContext);
        Assert.Equal("source-unit", batchKey.SourceUnitKey);
        Assert.Equal("udx/bldg/53394525/building.gml", batchKey.SourceFileRelativePath);
        Assert.Equal(
            "53394525|bldg|2|source-unit|udx/bldg/53394525/building.gml",
            batchKey.BatchScopeIdentity);
    }

    [Fact]
    public void TryCreateMaterialBySubmeshIndexReturnsFalseForDuplicateAssignments()
    {
        ImportedCityObject cityObject = CreateCityObject(materials:
        [
            CreateDatasetMaterial("shared-material", [0]),
            CreateDatasetMaterial("duplicate-material", [0]),
        ]);

        bool created = NonDemBatching.TryCreateMaterialBySubmeshIndex(cityObject, out Dictionary<int, MaterialBinding>? materialBySubmeshIndex);

        Assert.False(created);
        Assert.NotNull(materialBySubmeshIndex);
    }

    [Fact]
    public void CanBufferCityObjectMaterialsReturnsFalseWhenSubmeshMaterialIsMissing()
    {
        ImportedCityObject cityObject = CreateCityObject(materials: []);

        bool canBuffer = NonDemBatching.CanBufferCityObjectMaterials(cityObject, DefaultPolicy);

        Assert.False(canBuffer);
    }

    [Fact]
    public void ClassifyMaterialReturnsAtlasCandidateForPerObjectDatasetTexture()
    {
        NonDemBatchMaterialCategory category = NonDemBatching.ClassifyMaterial(CreateDatasetMaterial("atlas-material", [0]));

        Assert.Equal(NonDemBatchMaterialCategory.AtlasCandidate, category);
    }

    [Fact]
    public void ClassifyMaterialReturnsPreservedSharedMaterialForBundledSharedFamily()
    {
        MaterialBinding material = CreateSharedBundledMaterial("shared-roof", [0]);

        NonDemBatchMaterialCategory category = NonDemBatching.ClassifyMaterial(material);

        Assert.Equal(NonDemBatchMaterialCategory.PreservedSharedMaterial, category);
    }

    [Fact]
    public void ClassifyMaterialReturnsPreservedSharedMaterialForTintedFamilyDatasetTexture()
    {
        MaterialBinding material = CreateDatasetMaterial("tinted-family", [0]) with
        {
            Family = BundledDefaultMaterialFamilies.Facade,
            BaseColor = new ColorRgba(0.8, 0.8, 0.8, 1.0),
        };

        NonDemBatchMaterialCategory category = NonDemBatching.ClassifyMaterial(material);

        Assert.Equal(NonDemBatchMaterialCategory.PreservedSharedMaterial, category);
    }

    [Fact]
    public void CanBufferCityObjectMaterialsRejectsSharedMaterialWhenPolicyDisablesSharedPreservation()
    {
        ImportedCityObject cityObject = CreateCityObject(materials:
        [
            CreateDatasetMaterial("atlas-material", [0]),
            CreateSharedBundledMaterial("shared-roof", [1]),
        ], submeshes:
        [
            new MeshSubmesh(0, "atlas-material", [0, 1, 2]),
            new MeshSubmesh(1, "shared-roof", [0, 2, 3]),
        ]);
        NonDemBatchMaterialPolicy policy = DefaultPolicy with { PreserveSharedMaterials = false };

        bool canBuffer = NonDemBatching.CanBufferCityObjectMaterials(cityObject, policy);

        Assert.False(canBuffer);
    }

    [Fact]
    public void CanBufferCityObjectMaterialsRejectsTexturelessMaterialWhenPolicyDisablesTexturelessPreservation()
    {
        ImportedCityObject cityObject = CreateCityObject(materials:
        [
            CreateDatasetMaterial("atlas-material", [0]),
            CreateTexturelessMaterial("textureless", [1]),
        ], submeshes:
        [
            new MeshSubmesh(0, "atlas-material", [0, 1, 2]),
            new MeshSubmesh(1, "textureless", [0, 2, 3]),
        ]);
        NonDemBatchMaterialPolicy policy = DefaultPolicy with { PreserveTexturelessMaterials = false };

        bool canBuffer = NonDemBatching.CanBufferCityObjectMaterials(cityObject, policy);

        Assert.False(canBuffer);
    }

    [Fact]
    public void CanBufferCityObjectMaterialsRequiresAtlasCandidateWhenPolicyRequestsIt()
    {
        ImportedCityObject cityObject = CreateCityObject(materials:
        [
            CreateSharedBundledMaterial("shared-roof", [0]),
        ]);

        bool canBuffer = NonDemBatching.CanBufferCityObjectMaterials(cityObject, DefaultPolicy);

        Assert.False(canBuffer);
    }

    private static ImportedCityObject CreateCityObject(
        string objectKey = "building",
        string? sourceUnitKey = "source-unit",
        string? sourceFileRelativePath = "udx/bldg/53394525/building.gml",
        IReadOnlyList<MaterialBinding>? materials = null,
        IReadOnlyList<MeshSubmesh>? submeshes = null)
    {
        IReadOnlyList<MeshSubmesh> resolvedSubmeshes = submeshes
            ?? [new MeshSubmesh(0, "atlas-material", [0, 1, 2])];

        return new ImportedCityObject(
            ObjectKey: objectKey,
            DisplayName: objectKey,
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 2,
            Transform: new Transform3D(new Float3(0.0, 0.0, 0.0)),
            Mesh: new ImportedMesh(
                [
                    new MeshVertex(new Float3(0.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 0.0)),
                    new MeshVertex(new Float3(1.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(1.0, 0.0)),
                    new MeshVertex(new Float3(0.0, 1.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 1.0)),
                    new MeshVertex(new Float3(1.0, 1.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(1.0, 1.0)),
                ],
                resolvedSubmeshes),
            Materials: materials ?? [CreateDatasetMaterial("atlas-material", [0])],
            SourceObjectKey: $"source:{objectKey}",
            SourceUnitKey: sourceUnitKey,
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private static MaterialBinding CreateDatasetMaterial(string materialKey, IReadOnlyList<int> submeshIndices)
    {
        return new MaterialBinding(
            MaterialKey: materialKey,
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            MaterialType: MaterialType.Standard,
            TexturePayload: new TexturePayload(2, 2, "srgb", new byte[16], $"{materialKey}.png"),
            TextureSourceKind: TextureSourceKind.Dataset,
            Projection: MaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: submeshIndices);
    }

    private static MaterialBinding CreateSharedBundledMaterial(string materialKey, IReadOnlyList<int> submeshIndices)
    {
        return new MaterialBinding(
            MaterialKey: materialKey,
            BaseColor: new ColorRgba(0.85, 0.85, 0.85, 1.0),
            MaterialType: MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: TextureSourceKind.Bundled,
            Projection: MaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: submeshIndices,
            Family: BundledDefaultMaterialFamilies.Roof,
            ReuseScope: MaterialReuseScope.Shared,
            BundledVariantIndex: 0);
    }

    private static MaterialBinding CreateTexturelessMaterial(string materialKey, IReadOnlyList<int> submeshIndices)
    {
        return new MaterialBinding(
            MaterialKey: materialKey,
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            MaterialType: MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: TextureSourceKind.Dataset,
            Projection: MaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: submeshIndices);
    }
}
