using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
namespace PlateauResoniteLink.Tests.Profiles;

public sealed class DefaultMaterialResolverTests
{
    private readonly DefaultMaterialResolver resolver = new();

    [Fact]
    public void ResolveMaterialUsesDatasetTextureWhenPresent()
    {
        TexturePayload payload = new(4, 4, "srgb", new byte[4 * 4 * 4], "udx/bldg/53394525/appearance/roof.png");

        ResolvedMaterial material = resolver.ResolveMaterial(new DefaultMaterialRequest(
            "bldg",
            TexturePayload: payload,
            PreferUvProjection: true,
            FamilyOverride: null,
            VariantSelectionKey: "bldg:uv",
            SurfaceRole: DefaultMaterialSurfaceRole.Wall));

        Assert.Equal(MaterialType.Standard, material.MaterialType);
        Assert.Same(payload, material.TexturePayload);
        Assert.Equal(TextureSourceKind.Dataset, material.TextureSourceKind);
        Assert.Equal(MaterialProjection.Uv, material.Projection);
        Assert.Null(material.Family);
        Assert.Null(material.TextureScale);
        Assert.Equal(MaterialReuseScope.PerObject, material.ReuseScope);
    }

    [Theory]
    [InlineData((int)DefaultMaterialSurfaceRole.Wall)]
    [InlineData((int)DefaultMaterialSurfaceRole.Closure)]
    [InlineData((int)DefaultMaterialSurfaceRole.Unknown)]
    [InlineData((int)DefaultMaterialSurfaceRole.Roof)]
    public void ResolveMaterialUsesDatasetTextureBeforeBuildingFallback(int surfaceRoleValue)
    {
        DefaultMaterialSurfaceRole surfaceRole = (DefaultMaterialSurfaceRole)surfaceRoleValue;
        TexturePayload payload = new(4, 4, "srgb", new byte[4 * 4 * 4], $"udx/bldg/53394525/appearance/{surfaceRole}.png");

        ResolvedMaterial material = resolver.ResolveMaterial(new DefaultMaterialRequest(
            "bldg",
            TexturePayload: payload,
            PreferUvProjection: true,
            FamilyOverride: null,
            VariantSelectionKey: $"bldg:{surfaceRole}:texture",
            SurfaceRole: surfaceRole));

        Assert.Equal(MaterialType.Standard, material.MaterialType);
        Assert.Same(payload, material.TexturePayload);
        Assert.Equal(TextureSourceKind.Dataset, material.TextureSourceKind);
        Assert.Equal(MaterialProjection.Uv, material.Projection);
        Assert.Null(material.Family);
        Assert.Null(material.TextureScale);
        Assert.Null(material.TextureOffset);
        Assert.Equal(MaterialReuseScope.PerObject, material.ReuseScope);
    }

    [Fact]
    public void ResolveMaterialFallsBackToBundledFacadeForBuildingUvProjection()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(CreateBuildingWallRequest("bldg:uv"));
        string texturePath = BundledDefaultMaterialFamilies.GetVariant(
            BundledDefaultMaterialFamilies.WallResidentialPlasterLow,
            material.BundledVariantIndex!.Value);
        BundledDefaultMaterialProfile profile = BundledDefaultMaterialProfiles.GetProfile(texturePath);

        Assert.Equal(MaterialType.Standard, material.MaterialType);
        Assert.Null(material.TexturePayload);
        Assert.Equal(TextureSourceKind.Bundled, material.TextureSourceKind);
        Assert.Equal(MaterialProjection.Uv, material.Projection);
        Assert.Equal(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, material.Family);
        Assert.Equal(
            new Float2(
                profile.TextureScale.X,
                profile.TextureScale.Y),
            material.TextureScale);
        Assert.Equal(profile.TextureOffset is null ? null : new Float2(profile.TextureOffset.X, profile.TextureOffset.Y), material.TextureOffset);
        Assert.Equal(MaterialReuseScope.Shared, material.ReuseScope);
    }

    [Theory]
    [InlineData((int)DefaultMaterialSurfaceRole.Wall)]
    [InlineData((int)DefaultMaterialSurfaceRole.Closure)]
    [InlineData((int)DefaultMaterialSurfaceRole.Unknown)]
    public void ResolveMaterialUsesFacadeForWallLikeBuildingSurfaces(int surfaceRoleValue)
    {
        DefaultMaterialSurfaceRole surfaceRole = (DefaultMaterialSurfaceRole)surfaceRoleValue;

        ResolvedMaterial material = resolver.ResolveMaterial(CreateBuildingWallRequest(
            $"bldg:{surfaceRole}:fallback",
            surfaceRole: surfaceRole));

        Assert.Contains(BundledDefaultMaterialFamilies.BuildingFacadeFamilies, family => family == material.Family);
        Assert.Equal(MaterialProjection.Uv, material.Projection);
        Assert.Equal(TextureSourceKind.Bundled, material.TextureSourceKind);
        Assert.Null(material.TexturePayload);
    }

    [Theory]
    [InlineData((int)DefaultMaterialSurfaceRole.Roof)]
    [InlineData((int)DefaultMaterialSurfaceRole.Ground)]
    [InlineData((int)DefaultMaterialSurfaceRole.OuterCeiling)]
    [InlineData((int)DefaultMaterialSurfaceRole.OuterFloor)]
    public void ResolveMaterialDoesNotUseFacadeForNonWallBuildingSurfaces(int surfaceRoleValue)
    {
        DefaultMaterialSurfaceRole surfaceRole = (DefaultMaterialSurfaceRole)surfaceRoleValue;
        ResolvedMaterial material = resolver.ResolveMaterial(new DefaultMaterialRequest(
            "bldg",
            TexturePayload: null,
            PreferUvProjection: true,
            FamilyOverride: null,
            VariantSelectionKey: $"bldg:{surfaceRole}:fallback",
            SurfaceRole: surfaceRole));

        Assert.Equal(MaterialType.Standard, material.MaterialType);
        Assert.Null(material.TexturePayload);
        Assert.Equal(TextureSourceKind.Bundled, material.TextureSourceKind);
        Assert.Equal(MaterialProjection.Uv, material.Projection);
        Assert.Equal(BundledDefaultMaterialFamilies.Roof, material.Family);
        Assert.Equal(MaterialReuseScope.Shared, material.ReuseScope);
        Assert.DoesNotContain(BundledDefaultMaterialFamilies.BuildingFacadeFamilies, family => family == material.Family);
    }

    [Fact]
    public void ResolveMaterialUsesWireframeForOverlayPackages()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(new DefaultMaterialRequest(
            "luse",
            TexturePayload: null,
            PreferUvProjection: false,
            FamilyOverride: null,
            VariantSelectionKey: "luse:tri"));

        Assert.Equal(MaterialType.Wireframe, material.MaterialType);
        Assert.Null(material.TexturePayload);
        Assert.Equal(TextureSourceKind.Bundled, material.TextureSourceKind);
        Assert.Equal(MaterialProjection.Uv, material.Projection);
        Assert.Null(material.Family);
        Assert.Equal(MaterialReuseScope.PerObject, material.ReuseScope);
    }

    [Fact]
    public void ResolveMaterialUsesCityFurnitureFallbackFamily()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(CreateFallbackRequest("frn", "frn:tri"));

        Assert.Equal(MaterialType.Standard, material.MaterialType);
        Assert.Null(material.TexturePayload);
        Assert.Equal(TextureSourceKind.Bundled, material.TextureSourceKind);
        Assert.Equal(MaterialProjection.Triplanar, material.Projection);
        Assert.Equal(BundledDefaultMaterialFamilies.CityFurniture, material.Family);
        Assert.NotNull(material.TextureScale);
        Assert.Equal(
            ToContractFloat2(BundledDefaultMaterialProfiles.GetTilesPerMeterValue(BundledDefaultMaterialFamilies.GetVariants(BundledDefaultMaterialFamilies.CityFurniture)[0])),
            material.TextureScale);
        Assert.Equal(MaterialReuseScope.Shared, material.ReuseScope);
    }

    [Fact]
    public void ResolveMaterialUsesRoadFamilyForPathLikePackageWithoutTexture()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(CreateFallbackRequest("wwy", "wwy:tri"));

        Assert.Equal(MaterialType.Standard, material.MaterialType);
        Assert.Equal(BundledDefaultMaterialFamilies.RoadTriplanar, material.Family);
        Assert.Equal(MaterialReuseScope.Shared, material.ReuseScope);
    }

    [Fact]
    public void ResolveMaterialCanReachEveryRoadVariant()
    {
        Dictionary<int, ResolvedMaterial> materialsByVariant = [];
        for (int attempt = 0; attempt < 256 && materialsByVariant.Count < BundledDefaultMaterialFamilies.RoadVariants.Count; attempt++)
        {
            string variantSelectionKey = $"tran:tri:{attempt}";
            ResolvedMaterial material = resolver.ResolveMaterial(CreateFallbackRequest("tran", variantSelectionKey));
            materialsByVariant.TryAdd(material.BundledVariantIndex!.Value, material);
        }

        Assert.Equal(BundledDefaultMaterialFamilies.RoadVariants.Count, materialsByVariant.Count);
        Assert.All(
            materialsByVariant.Values,
            material =>
            {
                Assert.Equal(BundledDefaultMaterialFamilies.RoadTriplanar, material.Family);
                Assert.StartsWith(
                    "default-materials/ambientcg/road/Road",
                    BundledDefaultMaterialFamilies.GetVariant(material.Family!, material.BundledVariantIndex!.Value),
                    System.StringComparison.Ordinal);
            });
    }

    [Fact]
    public void ResolveMaterialCanReachEveryCityFurnitureVariant()
    {
        Dictionary<int, ResolvedMaterial> materialsByVariant = [];
        for (int attempt = 0; attempt < 512 && materialsByVariant.Count < BundledDefaultMaterialFamilies.CityFurnitureVariants.Count; attempt++)
        {
            string variantSelectionKey = $"frn:tri:{attempt}";
            ResolvedMaterial material = resolver.ResolveMaterial(CreateFallbackRequest("frn", variantSelectionKey));
            materialsByVariant.TryAdd(material.BundledVariantIndex!.Value, material);
        }

        Assert.Equal(BundledDefaultMaterialFamilies.CityFurnitureVariants.Count, materialsByVariant.Count);
        Assert.All(
            materialsByVariant.Values,
            material =>
            {
                Assert.Equal(BundledDefaultMaterialFamilies.CityFurniture, material.Family);
                Assert.Contains(
                    "/Plaster",
                    BundledDefaultMaterialFamilies.GetVariant(material.Family!, material.BundledVariantIndex!.Value),
                    System.StringComparison.Ordinal);
            });
    }

    [Fact]
    public void ResolveMaterialCanReachTextureCanGenericOtherVariant()
    {
        ResolvedMaterial? textureCanMaterial = null;
        for (int attempt = 0; attempt < 512 && textureCanMaterial is null; attempt++)
        {
            ResolvedMaterial material = resolver.ResolveMaterial(CreateFallbackRequest("brid", $"brid:tri:{attempt}"));
            string texturePath = BundledDefaultMaterialFamilies.GetVariant(material.Family!, material.BundledVariantIndex!.Value);
            if (texturePath.StartsWith("default-materials/texturecan/", System.StringComparison.Ordinal))
            {
                textureCanMaterial = material;
            }
        }

        Assert.NotNull(textureCanMaterial);
        Assert.Equal(BundledDefaultMaterialFamilies.Other, textureCanMaterial!.Family);
        Assert.Equal(MaterialReuseScope.Shared, textureCanMaterial.ReuseScope);
    }

    [Fact]
    public void ResolveMaterialUsesStableBundledVariantSelection()
    {
        ResolvedMaterial first = resolver.ResolveMaterial(
            CreateBuildingWallRequest("bldg:uv"));
        ResolvedMaterial second = resolver.ResolveMaterial(
            CreateBuildingWallRequest("bldg:uv"));

        Assert.Equal(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, first.Family);
        Assert.Equal(first.BundledVariantIndex, second.BundledVariantIndex);
        Assert.Equal(first.TextureScale, second.TextureScale);
        Assert.Equal(first.TextureOffset, second.TextureOffset);
        Assert.Equal(
            ToContractFloat2(BundledDefaultMaterialProfiles.GetProfile(
                BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, first.BundledVariantIndex!.Value)).TextureScale),
            first.TextureScale);
        Assert.Equal(
            BundledDefaultMaterialProfiles.GetProfile(
                BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, first.BundledVariantIndex!.Value)).TextureOffset is { } profileOffset
                ? new Float2(profileOffset.X, profileOffset.Y)
                : null,
            first.TextureOffset);
    }

    [Fact]
    public void ResolveMaterialCanReachEveryGeneratedFacadeVariantWithFacadeUvProfile()
    {
        Dictionary<int, ResolvedMaterial> materialsByVariant = [];
        for (int attempt = 0; attempt < 256 && materialsByVariant.Count < BundledDefaultMaterialFamilies.WallResidentialPlasterLowVariants.Count; attempt++)
        {
            string variantSelectionKey = $"bldg:uv:{attempt}";
            ResolvedMaterial material = resolver.ResolveMaterial(CreateBuildingWallRequest(variantSelectionKey));
            materialsByVariant.TryAdd(material.BundledVariantIndex!.Value, material);
        }

        Assert.Equal(BundledDefaultMaterialFamilies.WallResidentialPlasterLowVariants.Count, materialsByVariant.Count);
        foreach (ResolvedMaterial material in materialsByVariant.Values)
        {
            string texturePath = BundledDefaultMaterialFamilies.GetVariant(material.Family!, material.BundledVariantIndex!.Value);
            BundledDefaultMaterialProfile profile = BundledDefaultMaterialProfiles.GetProfile(texturePath);
            Assert.Equal(new Float2(profile.TextureScale.X, profile.TextureScale.Y), material.TextureScale);
            Assert.Equal(profile.TextureOffset is null ? null : new Float2(profile.TextureOffset.X, profile.TextureOffset.Y), material.TextureOffset);
            Assert.Equal(MaterialReuseScope.Shared, material.ReuseScope);
        }
    }

    [Theory]
    [InlineData((int)PlateauBuildingUse.DetachedResidential, BundledDefaultMaterialFamilies.WallResidentialPlasterLow)]
    [InlineData((int)PlateauBuildingUse.Apartment, BundledDefaultMaterialFamilies.WallResidentialTileLow)]
    [InlineData((int)PlateauBuildingUse.Office, BundledDefaultMaterialFamilies.WallCommercialPanel)]
    [InlineData((int)PlateauBuildingUse.Commercial, BundledDefaultMaterialFamilies.WallCommercialPanel)]
    [InlineData((int)PlateauBuildingUse.Public, BundledDefaultMaterialFamilies.WallSchoolPublicBand)]
    [InlineData((int)PlateauBuildingUse.Education, BundledDefaultMaterialFamilies.WallSchoolPublicBand)]
    [InlineData((int)PlateauBuildingUse.Warehouse, BundledDefaultMaterialFamilies.WallFactoryMetal)]
    [InlineData((int)PlateauBuildingUse.Factory, BundledDefaultMaterialFamilies.WallFactoryMetal)]
    public void ResolveMaterialSelectsFacadeFromBuildingUse(int useValue, string expectedFamily)
    {
        PlateauBuildingUse use = (PlateauBuildingUse)useValue;

        ResolvedMaterial material = resolver.ResolveMaterial(CreateBuildingWallRequest(
            $"bldg:usage:{use}",
            BuildingAttributeContext.Empty with
            {
                Uses = [new BuildingCodeValue<PlateauBuildingUse>(use, "test")],
            }));

        Assert.Equal(expectedFamily, material.Family);
    }

    [Fact]
    public void ResolveMaterialSelectsRuralFacadeFromAgriculturalCode()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(CreateBuildingWallRequest(
            "bldg:usage:451",
            BuildingAttributeContext.Empty with { CityGmlFunctionCodes = ["451"] }));

        Assert.Equal(BundledDefaultMaterialFamilies.WallWoodRural, material.Family);
    }

    [Fact]
    public void ResolveMaterialSelectsBrickFacadeFromConcreteBlockStructure()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(CreateBuildingWallRequest(
            "bldg:structure:606",
            BuildingAttributeContext.Empty with
            {
                Structures =
                [
                    new BuildingCodeValue<PlateauBuildingStructure>(PlateauBuildingStructure.ConcreteBlock, "606"),
                ],
            }));

        Assert.Equal(BundledDefaultMaterialFamilies.WallBrickRetro, material.Family);
    }

    [Fact]
    public void ResolveMaterialSelectsRcFacadeForUnknownMidRise()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(CreateBuildingWallRequest(
            "bldg:unknown:midrise",
            BuildingAttributeContext.Empty,
            floorsAboveGround: 5,
            measuredHeightMeters: 16.0));

        Assert.Equal(BundledDefaultMaterialFamilies.WallRcPaintedMid, material.Family);
    }

    [Fact]
    public void ResolveMaterialUsesGeometryHeightWhenMeasuredHeightIsMissing()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(CreateBuildingWallRequest(
            "bldg:geometry-height:landmark",
            BuildingAttributeContext.Empty with
            {
                Uses = [new BuildingCodeValue<PlateauBuildingUse>(PlateauBuildingUse.Office, "401")],
            },
            geometryHeightMeters: 296.0));

        Assert.Equal(BundledDefaultMaterialFamilies.FacadeHighriseGlass, material.Family);
    }

    [Theory]
    [InlineData((int)PlateauBuildingUse.Office, BundledDefaultMaterialFamilies.FacadeHighriseGlass)]
    [InlineData((int)PlateauBuildingUse.Commercial, BundledDefaultMaterialFamilies.FacadeHighriseGlass)]
    [InlineData((int)PlateauBuildingUse.Apartment, BundledDefaultMaterialFamilies.FacadeHighriseNightLow)]
    [InlineData((int)PlateauBuildingUse.MixedResidential, BundledDefaultMaterialFamilies.FacadeHighriseNightLow)]
    public void ResolveMaterialSelectsHighriseFacadeFromUseAndNightOccupancy(int useValue, string expectedFamily)
    {
        PlateauBuildingUse use = (PlateauBuildingUse)useValue;

        ResolvedMaterial material = resolver.ResolveMaterial(CreateBuildingWallRequest(
            $"bldg:highrise:{use}",
            BuildingAttributeContext.Empty with
            {
                Uses = [new BuildingCodeValue<PlateauBuildingUse>(use, "test")],
            },
            measuredHeightMeters: 95.0));

        Assert.Equal(expectedFamily, material.Family);
    }

    [Fact]
    public void ResolveMaterialTreatsRaw403AsNightOccupiedHighrise()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(CreateBuildingWallRequest(
            "bldg:highrise:403",
            BuildingAttributeContext.Empty with { CityGmlFunctionCodes = ["403"] },
            measuredHeightMeters: 95.0));

        Assert.Equal(BundledDefaultMaterialFamilies.FacadeHighriseNightLow, material.Family);
    }

    [Theory]
    [InlineData((int)PlateauBuildingUse.Office, BundledDefaultMaterialFamilies.FacadeMidriseGrid)]
    [InlineData((int)PlateauBuildingUse.Commercial, BundledDefaultMaterialFamilies.FacadeMidriseGrid)]
    [InlineData((int)PlateauBuildingUse.Apartment, BundledDefaultMaterialFamilies.FacadeMidriseGrid)]
    [InlineData((int)PlateauBuildingUse.MixedResidential, BundledDefaultMaterialFamilies.FacadeMidriseGrid)]
    public void ResolveMaterialSelectsMidriseFacadeFromUse(int useValue, string expectedFamily)
    {
        PlateauBuildingUse use = (PlateauBuildingUse)useValue;

        ResolvedMaterial material = resolver.ResolveMaterial(CreateBuildingWallRequest(
            $"bldg:midrise:{use}",
            BuildingAttributeContext.Empty with
            {
                Uses = [new BuildingCodeValue<PlateauBuildingUse>(use, "test")],
            },
            measuredHeightMeters: 35.0));

        Assert.Equal(expectedFamily, material.Family);
    }

    [Fact]
    public void ResolveMaterialKeepsLowriseResidentialOnGeneratedFacade()
    {
        ResolvedMaterial material = resolver.ResolveMaterial(CreateBuildingWallRequest(
            "bldg:lowrise:residential",
            BuildingAttributeContext.Empty with
            {
                Uses = [new BuildingCodeValue<PlateauBuildingUse>(PlateauBuildingUse.DetachedResidential, "411")],
            },
            measuredHeightMeters: 9.0));

        Assert.Equal(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, material.Family);
    }

    [Fact]
    public void BuildingFacadeFamilyCatalogContainsOnlyResolverReachableFamilies()
    {
        HashSet<string> reachableFamilies = [];
        foreach (DefaultMaterialRequest request in CreateRepresentativeBuildingFacadeRequests())
        {
            reachableFamilies.Add(resolver.ResolveMaterial(request).Family!);
        }

        Assert.Equal(
            reachableFamilies.OrderBy(static family => family, StringComparer.Ordinal),
            BundledDefaultMaterialFamilies.BuildingFacadeFamilies.OrderBy(static family => family, StringComparer.Ordinal));
    }

    [Fact]
    public void BuildingFacadeFamiliesUseCuratedSubstanceChildAssets()
    {
        Assert.Equal<string>(
            [
                "default-materials/ambientcg/facade/Facade001_2K-JPG_Color.jpg",
                "default-materials/ambientcg/facade/Facade005_2K-JPG_Color.jpg",
                "default-materials/ambientcg/facade/Facade006_2K-JPG_Color.jpg",
            ],
            BundledDefaultMaterialFamilies.FacadeHighriseGlassVariants.Select(static variant => variant.TexturePath));
        Assert.Equal<string>(
            [
                "default-materials/ambientcg/facade/Facade002_2K-JPG_Color.jpg",
                "default-materials/ambientcg/facade/Facade011_2K-JPG_Color.jpg",
            ],
            BundledDefaultMaterialFamilies.FacadeHighriseNightLowVariants.Select(static variant => variant.TexturePath));
        Assert.Equal<string>(
            [
                "default-materials/ambientcg/facade/Facade014_2K-JPG_Color.jpg",
                "default-materials/ambientcg/facade/Facade015_2K-JPG_Color.jpg",
            ],
            BundledDefaultMaterialFamilies.FacadeMidriseGridVariants.Select(static variant => variant.TexturePath));
    }

    [Fact]
    public void ResolveMaterialRejectsExplicitFacadeOverrideOutsideCodebaseReachableFamilies()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => resolver.ResolveMaterial(new DefaultMaterialRequest(
                "bldg",
                TexturePayload: null,
                PreferUvProjection: true,
                FamilyOverride: BundledDefaultMaterialFamilies.Facade,
                VariantSelectionKey: "bldg:uv:0",
                SurfaceRole: DefaultMaterialSurfaceRole.Wall)));

        Assert.Contains("not codebase-reachable", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveMaterialAppliesGeneratedFacadeFloorOffset()
    {
        ResolvedMaterial first = resolver.ResolveMaterial(CreateBuildingWallRequest("bldg:wall:one"));
        ResolvedMaterial? different = null;
        for (int attempt = 0; attempt < 64 && different is null; attempt++)
        {
            ResolvedMaterial candidate = resolver.ResolveMaterial(CreateBuildingWallRequest($"bldg:wall:other:{attempt}"));
            if (candidate.BundledVariantIndex != first.BundledVariantIndex)
            {
                different = candidate;
            }
        }

        Assert.NotNull(different);
        string firstPath = BundledDefaultMaterialFamilies.GetVariant(first.Family!, first.BundledVariantIndex!.Value);
        string differentPath = BundledDefaultMaterialFamilies.GetVariant(different!.Family!, different.BundledVariantIndex!.Value);
        Assert.Equal(new ScalarPair(0.0, 0.5 / 6.0), BundledDefaultMaterialProfiles.GetProfile(firstPath).TextureOffset);
        Assert.Equal(new ScalarPair(0.0, 0.5 / 6.0), BundledDefaultMaterialProfiles.GetProfile(differentPath).TextureOffset);
        Assert.Equal(new Float2(0.0, 0.5 / 6.0), first.TextureOffset);
        Assert.Equal(new Float2(0.0, 0.5 / 6.0), different.TextureOffset);
    }

    private static Float2 ToContractFloat2(ScalarPair value) => new(value.X, value.Y);

    private static DefaultMaterialRequest CreateFallbackRequest(string packageName, string variantSelectionKey)
    {
        return new DefaultMaterialRequest(
            packageName,
            TexturePayload: null,
            PreferUvProjection: false,
            FamilyOverride: null,
            VariantSelectionKey: variantSelectionKey);
    }

    private static DefaultMaterialRequest CreateBuildingWallRequest(
        string variantSelectionKey,
        BuildingAttributeContext? attributes = null,
        int? floorsAboveGround = null,
        double? measuredHeightMeters = null,
        double? geometryHeightMeters = null,
        DefaultMaterialSurfaceRole surfaceRole = DefaultMaterialSurfaceRole.Wall)
    {
        return new DefaultMaterialRequest(
            "bldg",
            TexturePayload: null,
            PreferUvProjection: true,
            FamilyOverride: null,
            VariantSelectionKey: variantSelectionKey,
            BuildingAttributes: attributes,
            FloorsAboveGround: floorsAboveGround,
            MeasuredHeightMeters: measuredHeightMeters,
            GeometryHeightMeters: geometryHeightMeters,
            SurfaceRole: surfaceRole);
    }

    private static IEnumerable<DefaultMaterialRequest> CreateRepresentativeBuildingFacadeRequests()
    {
        yield return CreateBuildingWallRequest(
            "bldg:catalog:detached",
            BuildingAttributeContext.Empty with { Uses = [new BuildingCodeValue<PlateauBuildingUse>(PlateauBuildingUse.DetachedResidential, "411")] });
        yield return CreateBuildingWallRequest(
            "bldg:catalog:detached:alternate:10",
            BuildingAttributeContext.Empty with { Uses = [new BuildingCodeValue<PlateauBuildingUse>(PlateauBuildingUse.DetachedResidential, "411")] });
        yield return CreateBuildingWallRequest(
            "bldg:catalog:apartment",
            BuildingAttributeContext.Empty with { Uses = [new BuildingCodeValue<PlateauBuildingUse>(PlateauBuildingUse.Apartment, "412")] });
        yield return CreateBuildingWallRequest(
            "bldg:catalog:apartment:mid",
            BuildingAttributeContext.Empty with { Uses = [new BuildingCodeValue<PlateauBuildingUse>(PlateauBuildingUse.Apartment, "412")] },
            measuredHeightMeters: 16.0);
        yield return CreateBuildingWallRequest(
            "bldg:catalog:office",
            BuildingAttributeContext.Empty with { Uses = [new BuildingCodeValue<PlateauBuildingUse>(PlateauBuildingUse.Office, "401")] });
        yield return CreateBuildingWallRequest(
            "bldg:catalog:public",
            BuildingAttributeContext.Empty with { Uses = [new BuildingCodeValue<PlateauBuildingUse>(PlateauBuildingUse.Public, "421")] });
        yield return CreateBuildingWallRequest(
            "bldg:catalog:factory",
            BuildingAttributeContext.Empty with { Uses = [new BuildingCodeValue<PlateauBuildingUse>(PlateauBuildingUse.Factory, "441")] });
        yield return CreateBuildingWallRequest(
            "bldg:catalog:rural",
            BuildingAttributeContext.Empty with { CityGmlFunctionCodes = ["451"] });
        yield return CreateBuildingWallRequest(
            "bldg:catalog:brick",
            BuildingAttributeContext.Empty with
            {
                Structures =
                [
                    new BuildingCodeValue<PlateauBuildingStructure>(PlateauBuildingStructure.ConcreteBlock, "606"),
                ],
            });
        yield return CreateBuildingWallRequest(
            "bldg:catalog:rc",
            BuildingAttributeContext.Empty,
            floorsAboveGround: 5,
            measuredHeightMeters: 16.0);
        yield return CreateBuildingWallRequest(
            "bldg:catalog:highrise-office",
            BuildingAttributeContext.Empty with { Uses = [new BuildingCodeValue<PlateauBuildingUse>(PlateauBuildingUse.Office, "401")] },
            measuredHeightMeters: 95.0);
        yield return CreateBuildingWallRequest(
            "bldg:catalog:highrise-night",
            BuildingAttributeContext.Empty with { Uses = [new BuildingCodeValue<PlateauBuildingUse>(PlateauBuildingUse.Apartment, "412")] },
            measuredHeightMeters: 95.0);
        yield return CreateBuildingWallRequest(
            "bldg:catalog:midrise-grid",
            BuildingAttributeContext.Empty with { Uses = [new BuildingCodeValue<PlateauBuildingUse>(PlateauBuildingUse.Office, "401")] },
            measuredHeightMeters: 35.0);
    }
}
