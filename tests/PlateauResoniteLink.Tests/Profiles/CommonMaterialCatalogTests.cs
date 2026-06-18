using PlateauResoniteLink.Application.Importing.Contracts;
using PlateauResoniteLink.Application.Importing.Source;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class CommonMaterialCatalogTests
{
    [Fact]
    public void Create_IncludesCodebaseReachableAlbedoAndVertexColorCommonMaterials()
    {
        IReadOnlyList<MaterialBinding> materials = CreateMaterialCatalog();

        Assert.Contains(
            materials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.Standard
                && material.Projection == MaterialProjection.Uv
                && material.TexturePayload is null
                && material.TextureScale is null
                && material.TextureOffset is null
                && material.DepthOffset is null
                && material.Family is null);
        Assert.Contains(
            materials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.VertexColor
                && material.Projection == MaterialProjection.Uv
                && material.TexturePayload is null
                && material.TextureScale is null
                && material.TextureOffset is null
                && material.DepthOffset is null);
        Assert.Contains(
            materials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.VertexColor
                && material.Projection == MaterialProjection.Uv
                && material.TexturePayload is null
                && material.TextureScale is null
                && material.TextureOffset is null
                && material.DepthOffset == LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset);
    }

    [Fact]
    public void Create_UsesResolverReachableVariantProfilesForBundledCommonMaterials()
    {
        IReadOnlyList<MaterialBinding> materials = CreateMaterialCatalog();

        MaterialBinding roofMaterial = Assert.Single(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.Roof
                && material.Projection == MaterialProjection.Triplanar
                && material.BundledVariantIndex == 0);
        MaterialBinding wallMaterial = Assert.Single(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.WallResidentialPlasterLow
                && material.Projection == MaterialProjection.Uv
                && material.BundledVariantIndex == 0);
        BundledDefaultMaterialProfile wallProfile = BundledDefaultMaterialProfiles.GetProfile(
            BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, 0));

        Assert.Equal(
            new Float2(BundledDefaultMaterialProfiles.ConcreteDefaultTilesPerMeterValue.X, BundledDefaultMaterialProfiles.ConcreteDefaultTilesPerMeterValue.Y),
            roofMaterial.TextureScale);
        Assert.Equal(new Float2(wallProfile.TextureScale.X, wallProfile.TextureScale.Y), wallMaterial.TextureScale);
        Assert.Equal(
            wallProfile.TextureOffset is null
                ? null
                : new Float2(wallProfile.TextureOffset.X, wallProfile.TextureOffset.Y),
            wallMaterial.TextureOffset);
        Assert.DoesNotContain(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.Facade);
        Assert.DoesNotContain(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.Roof
                && material.Projection == MaterialProjection.Uv);
    }

    [Fact]
    public void BundledFacadeProfiles_DeclareFacadeFloorUnitScaleSemantic()
    {
        BundledDefaultMaterialProfile facadeProfile = BundledDefaultMaterialProfiles.GetProfile(
            BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.Facade, 0));
        BundledDefaultMaterialProfile roofProfile = BundledDefaultMaterialProfiles.GetProfile(
            BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.Roof, 0));

        Assert.Equal(BundledDefaultMaterialUvScaleSemantic.FacadeFloorUnits, facadeProfile.ScaleSemantic);
        Assert.Equal(BundledDefaultMaterialUvScaleSemantic.WorldMeters, roofProfile.ScaleSemantic);
    }

    [Fact]
    public void Create_IncludesCodebaseReachableBuildingFacadeFamilies()
    {
        IReadOnlyList<MaterialBinding> materials = CreateMaterialCatalog();

        foreach (string family in BundledDefaultMaterialFamilies.BuildingFacadeFamilies)
        {
            Assert.Equal(
                BundledDefaultMaterialFamilies.GetVariants(family).Count,
                materials.Count(material => material.Family == family && material.Projection == MaterialProjection.Uv));
            Assert.DoesNotContain(
                materials,
                material => material.Family == family && material.Projection == MaterialProjection.Triplanar);
        }
    }

    [Fact]
    public void Create_UsesVariantProfilesForBuildingFacadeFamilies()
    {
        IReadOnlyList<MaterialBinding> materials = CreateMaterialCatalog();

        foreach (string family in BundledDefaultMaterialFamilies.BuildingFacadeFamilies)
        {
            IReadOnlyList<string> variants = BundledDefaultMaterialFamilies.GetVariants(family);
            for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
            {
                BundledDefaultMaterialProfile profile = BundledDefaultMaterialProfiles.GetProfile(variants[variantIndex]);
                MaterialBinding material = Assert.Single(
                    materials,
                    candidate => candidate.Family == family
                        && candidate.Projection == MaterialProjection.Uv
                        && candidate.BundledVariantIndex == variantIndex);

                Assert.Equal(new Float2(profile.TextureScale.X, profile.TextureScale.Y), material.TextureScale);
                Assert.Equal(
                    profile.TextureOffset is null
                        ? null
                        : new Float2(profile.TextureOffset.X, profile.TextureOffset.Y),
                    material.TextureOffset);
            }
        }
    }

    [Fact]
    public void GeneratedFacadeProfiles_UseFacadeFloorUnitUvSemantic()
    {
        foreach (string family in BundledDefaultMaterialFamilies.BuildingFacadeFamilies)
        {
            foreach (string variant in BundledDefaultMaterialFamilies.GetVariants(family))
            {
                BundledDefaultMaterialProfile profile = BundledDefaultMaterialProfiles.GetProfile(variant);

                Assert.Equal(BundledDefaultMaterialUvScaleSemantic.FacadeFloorUnits, profile.ScaleSemantic);
                Assert.True(profile.TextureScale.X > 0.0);
                Assert.Equal(profile.TextureScale.X, profile.TextureScale.Y, 6);
            }
        }
    }

    [Fact]
    public void Create_UsesCanonicalFamilyVariantShapeForBundledCommonMaterials()
    {
        IReadOnlyList<MaterialBinding> materials = CreateMaterialCatalog();

        MaterialBinding wallMaterial = Assert.Single(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.WallResidentialPlasterLow
                && material.Projection == MaterialProjection.Uv
                && material.BundledVariantIndex == 0);
        MaterialBinding roofMaterial = Assert.Single(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.Roof
                && material.Projection == MaterialProjection.Triplanar
                && material.BundledVariantIndex == 0);

        Assert.Equal(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, wallMaterial.Family);
        Assert.Equal(0, wallMaterial.BundledVariantIndex);
        Assert.Equal(MaterialProjection.Uv, wallMaterial.Projection);
        Assert.Equal(BundledDefaultMaterialFamilies.Roof, roofMaterial.Family);
        Assert.Equal(0, roofMaterial.BundledVariantIndex);
        Assert.Equal(MaterialProjection.Triplanar, roofMaterial.Projection);
    }

    [Fact]
    public void Create_AssignsStableAndDistinctDefinitionsToCodebaseReachableCommonMaterials()
    {
        IReadOnlyList<MaterialBinding> firstMaterials = CommonMaterialCatalog.Create().Map(static member => member.CreateBinding([0])).EnumerateItems();
        IReadOnlyList<MaterialBinding> secondMaterials = CommonMaterialCatalog.Create().Map(static member => member.CreateBinding([0])).EnumerateItems();

        MaterialBinding sharedGeneric = Assert.Single(
            firstMaterials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.Standard
                && material.Family is null
                && material.TexturePayload is null
                && material.Projection == MaterialProjection.Uv
                && material.DepthOffset is null);
        MaterialBinding terrainAlignedSharedGeneric = Assert.Single(
            firstMaterials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.Standard
                && material.Family is null
                && material.TexturePayload is null
                && material.Projection == MaterialProjection.Uv
                && material.DepthOffset == LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset);
        MaterialBinding sharedVertexColor = Assert.Single(
            firstMaterials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.VertexColor
                && material.Projection == MaterialProjection.Uv
                && material.DepthOffset is null);
        MaterialBinding terrainAlignedSharedVertexColor = Assert.Single(
            firstMaterials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.VertexColor
                && material.Projection == MaterialProjection.Uv
                && material.DepthOffset == LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset);
        MaterialBinding repeatedSharedGeneric = Assert.Single(
            secondMaterials,
            material => material.ReuseScope == MaterialReuseScope.Shared
                && material.MaterialType == MaterialType.Standard
                && material.Family is null
                && material.TexturePayload is null
                && material.Projection == MaterialProjection.Uv
                && material.DepthOffset is null);

        Assert.Equal(CreateMaterialSignature(sharedGeneric), CreateMaterialSignature(repeatedSharedGeneric));
        Assert.NotEqual(sharedGeneric.MaterialType, sharedVertexColor.MaterialType);
        Assert.Null(sharedGeneric.DepthOffset);
        Assert.Equal(LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset, terrainAlignedSharedGeneric.DepthOffset);
        Assert.Null(sharedVertexColor.DepthOffset);
        Assert.Equal(LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset, terrainAlignedSharedVertexColor.DepthOffset);
        Assert.All(firstMaterials.Where(static material => material.Family is not null), static material =>
        {
            Assert.NotNull(material.Family);
            Assert.Equal(TextureSourceKind.Bundled, material.TextureSourceKind);
        });
    }

    [Fact]
    public void Create_DoesNotContainDuplicateMaterialDefinitions()
    {
        IReadOnlyList<MaterialBinding> materials = CreateMaterialCatalog();

        Assert.Equal(
            materials.Count,
            materials
                .Select(static material => new
                {
                    material.MaterialType,
                    material.TextureSourceKind,
                    material.Projection,
                    material.DepthOffset,
                    material.TextureScale,
                    material.Family,
                    material.TextureOffset,
                    material.BundledVariantIndex,
                    material.TerrainMeshCode,
                })
                .Distinct()
                .Count());
    }

    [Fact]
    public void Catalog_IsTypedTreeAndNotReadOnlyList()
    {
        CommonMaterialCatalog<DefaultCommonMaterialMember> catalog = CommonMaterialCatalog.Create();

        Assert.IsNotAssignableFrom<IReadOnlyList<DefaultCommonMaterialMember>>(catalog);
        Assert.Same(catalog.Generic.Uv, catalog.Get(catalog.Generic.Uv.Definition));
        Assert.Same(catalog.FacadeMidriseGrid.Facade014, catalog.Get(catalog.FacadeMidriseGrid.Facade014.Definition));
    }

    [Fact]
    public void Map_PreservesTypedMemberShapeAndTraversalCount()
    {
        int index = 0;
        CommonMaterialCatalog<int> catalog = new(_ => ++index);

        CommonMaterialCatalog<string> selected = catalog.Map(static value => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"item-{value}"));

        Assert.Equal(catalog.Count, selected.Count);
        Assert.Equal($"item-{catalog.CityFurniture.Plaster002}", selected.CityFurniture.Plaster002);
        Assert.Equal($"item-{catalog.Generic.Uv}", selected.Generic.Uv);
    }

    [Fact]
    public async Task MapAsync_PreservesTypedMemberShapeAndTraversalCount()
    {
        int index = 0;
        CommonMaterialCatalog<int> catalog = new(_ => ++index);

        CommonMaterialCatalog<string> selected = await catalog.MapAsync(
            static (value, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"item-{value}"));
            },
            CancellationToken.None);

        Assert.Equal(catalog.Count, selected.Count);
        Assert.Equal($"item-{catalog.CityFurniture.Plaster002}", selected.CityFurniture.Plaster002);
        Assert.Equal($"item-{catalog.Generic.Uv}", selected.Generic.Uv);
    }

    [Fact]
    public void Map_MapsOnlyFilteredActiveDefinitions()
    {
        CommonMaterialCatalog<DefaultCommonMaterialMember> catalog = CommonMaterialCatalog.Create();
        CommonMaterialDefinition activeDefinition = catalog.Generic.Uv.Definition;
        CommonMaterialCatalog<DefaultCommonMaterialMember> filtered = catalog.FilterToDefinitions([activeDefinition]);
        List<CommonMaterialDefinition> mappedDefinitions = [];

        CommonMaterialCatalog<string> mapped = filtered.Map(member =>
        {
            mappedDefinitions.Add(member.Definition);
            return member.Definition.MemberName;
        });

        CommonMaterialCatalogMember<string> mappedMember = Assert.Single(mapped.EnumerateMembers());
        Assert.Same(activeDefinition, mappedMember.Definition);
        Assert.Equal(activeDefinition.MemberName, mappedMember.Item);
        Assert.Equal([activeDefinition], mappedDefinitions);
    }

    [Fact]
    public async Task MapAsync_MapsOnlyFilteredActiveDefinitions()
    {
        CommonMaterialCatalog<DefaultCommonMaterialMember> catalog = CommonMaterialCatalog.Create();
        CommonMaterialDefinition activeDefinition = catalog.Generic.Uv.Definition;
        CommonMaterialCatalog<DefaultCommonMaterialMember> filtered = catalog.FilterToDefinitions([activeDefinition]);
        List<CommonMaterialDefinition> mappedDefinitions = [];

        CommonMaterialCatalog<string> mapped = await filtered.MapAsync(
            (member, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                mappedDefinitions.Add(member.Definition);
                return ValueTask.FromResult(member.Definition.MemberName);
            },
            CancellationToken.None);

        CommonMaterialCatalogMember<string> mappedMember = Assert.Single(mapped.EnumerateMembers());
        Assert.Same(activeDefinition, mappedMember.Definition);
        Assert.Equal(activeDefinition.MemberName, mappedMember.Item);
        Assert.Equal([activeDefinition], mappedDefinitions);
    }

    [Fact]
    public void FilterToDefinitions_RejectsDefinitionsOutsideCurrentActiveSet()
    {
        CommonMaterialCatalog<DefaultCommonMaterialMember> catalog = CommonMaterialCatalog.Create();
        CommonMaterialDefinition activeDefinition = catalog.Generic.Uv.Definition;
        CommonMaterialDefinition inactiveDefinition = catalog.VertexColor.Uv.Definition;
        CommonMaterialCatalog<DefaultCommonMaterialMember> filtered = catalog.FilterToDefinitions([activeDefinition]);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => filtered.FilterToDefinitions([inactiveDefinition]));

        Assert.Contains(inactiveDefinition.MemberName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("not active", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_IncludesOnlyResolverReachableRoadAndGenericVariants()
    {
        IReadOnlyList<MaterialBinding> materials = CreateMaterialCatalog();

        Assert.Equal(
            BundledDefaultMaterialFamilies.RoadVariants.Count,
            materials.Count(material => material.Family == BundledDefaultMaterialFamilies.RoadUv));
        Assert.Equal(
            BundledDefaultMaterialFamilies.RoadVariants.Count,
            materials.Count(material => material.Family == BundledDefaultMaterialFamilies.RoadTriplanar));
        Assert.Equal(
            BundledDefaultMaterialFamilies.VegetationVariants.Count,
            materials.Count(material => material.Family == BundledDefaultMaterialFamilies.Vegetation));
        Assert.Equal(
            BundledDefaultMaterialFamilies.CityFurnitureVariants.Count,
            materials.Count(material => material.Family == BundledDefaultMaterialFamilies.CityFurniture));
        Assert.Contains(
            materials,
            material => material.Family == BundledDefaultMaterialFamilies.Other
                && material.BundledVariantIndex == BundledDefaultMaterialFamilies.OtherVariants.Count - 1
                && material.Projection == MaterialProjection.Triplanar);
        Assert.DoesNotContain(
            materials,
            material => material.Family is BundledDefaultMaterialFamilies.Vegetation
                    or BundledDefaultMaterialFamilies.CityFurniture
                    or BundledDefaultMaterialFamilies.Other
                && material.Projection == MaterialProjection.Uv);
        Assert.DoesNotContain(
            BundledDefaultMaterialFamilies.OtherVariants,
            variant => variant.Albedo.LogicalPath.Contains("/facade/", StringComparison.Ordinal));
    }

    private static string CreateMaterialSignature(MaterialBinding material)
    {
        string submeshes = string.Join("/", material.SubmeshIndices);
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{material.BaseColor.R},{material.BaseColor.G},{material.BaseColor.B},{material.BaseColor.A}|"
            + $"{material.MaterialType}|{material.TextureSourceKind}|{material.Projection}|"
            + $"{material.DepthOffset?.Factor}:{material.DepthOffset?.Units}|"
            + $"{material.TextureScale?.X}:{material.TextureScale?.Y}|"
            + $"{material.Family}|{material.TextureOffset?.X}:{material.TextureOffset?.Y}|"
            + $"{material.ReuseScope}|{material.BundledVariantIndex}|{material.TerrainMeshCode}|{submeshes}");
    }

    private static IReadOnlyList<MaterialBinding> CreateMaterialCatalog()
    {
        return CommonMaterialCatalog.Create().Map(static member => member.CreateBinding([0])).EnumerateItems();
    }
}
