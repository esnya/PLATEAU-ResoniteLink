using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteCommonMaterialAssetAccumulatorTests
{
    [Fact]
    public void Set_ReplacesExistingTypedMemberWithoutDuplicatingCatalogEntry()
    {
        DefaultCommonMaterialMember generic = CommonMaterialCatalog.Create().Generic.Uv;
        DefaultCommonMaterialMember vertexColor = CommonMaterialCatalog.Create().VertexColor.Uv;
        ResoniteMaterialBinding genericMaterial = SceneImportContractMapper.ToInternal(generic.CreateBinding([0]));
        ResoniteMaterialBinding vertexColorMaterial = SceneImportContractMapper.ToInternal(vertexColor.CreateBinding([1]));
        ResoniteCommonMaterialAssetAccumulator accumulator = new();

        accumulator.Set(new ResoniteCommonMaterialAsset(
            generic,
            genericMaterial,
            new CreatedMaterialAsset(new ResoniteComponentLocator("old-generic"), null)));
        accumulator.Set(new ResoniteCommonMaterialAsset(
            vertexColor,
            vertexColorMaterial,
            new CreatedMaterialAsset(new ResoniteComponentLocator("vertex-color"), null)));
        accumulator.Set(new ResoniteCommonMaterialAsset(
            generic,
            genericMaterial,
            new CreatedMaterialAsset(new ResoniteComponentLocator("new-generic"), null)));

        CommonMaterialCatalog<ResoniteCommonMaterialAsset> catalog = accumulator.ToCatalog();

        Assert.Equal(2, accumulator.Count);
        Assert.Equal(generic, catalog.Generic.Uv.Member);
        Assert.Equal("new-generic", catalog.Generic.Uv.Asset.MaterialComponent.Value);
        Assert.Equal(vertexColor, catalog.VertexColor.Uv.Member);
        Assert.Equal("vertex-color", catalog.VertexColor.Uv.Asset.MaterialComponent.Value);
    }
}
