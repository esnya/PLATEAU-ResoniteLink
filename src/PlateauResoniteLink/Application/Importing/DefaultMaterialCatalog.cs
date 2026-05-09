using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

using System.Security.Cryptography;
using System.Text;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class DefaultMaterialResolver : IDefaultMaterialResolver
{
    public ResolvedMaterial ResolveMaterial(DefaultMaterialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ResolveMaterialCore(request);
    }

    internal static ResolvedMaterial ResolveMaterialCore(DefaultMaterialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ShouldUseWireframeMaterial(request.PackageName))
        {
            return new ResolvedMaterial(
                MaterialType.Wireframe,
                TexturePayload: null,
                TextureSourceKind.Bundled,
                MaterialProjection.Uv,
                Family: null,
                TextureScale: null,
                ReuseScope: MaterialReuseScope.PerObject);
        }

        if (request.TexturePayload is not null)
        {
            return new ResolvedMaterial(
                MaterialType.Standard,
                request.TexturePayload,
                TextureSourceKind.Dataset,
                MaterialProjection.Uv,
                Family: null,
                TextureScale: null,
                ReuseScope: MaterialReuseScope.PerObject);
        }

        bool useWallSkin = ShouldUseBuildingWallSkin(request);
        string family = request.FamilyOverride ?? ResolveBundledTextureFamily(request, useWallSkin);
        int bundledVariantIndex = SelectBundledVariantIndex(family, request.VariantSelectionKey);
        string texturePath = BundledDefaultMaterialFamilies.GetVariant(family, bundledVariantIndex);
        BundledDefaultMaterialProfile uvProfile = BundledDefaultMaterialProfiles.GetProfile(texturePath);
        Float2? textureOffset = uvProfile.TextureOffset is null ? null : ToContractFloat2(uvProfile.TextureOffset);

        return new ResolvedMaterial(
            MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind.Bundled,
            request.PreferUvProjection ? MaterialProjection.Uv : MaterialProjection.Triplanar,
            family,
            ToContractFloat2(uvProfile.TextureScale),
            MaterialReuseScope.Shared,
            BundledVariantIndex: bundledVariantIndex,
            TextureOffset: textureOffset);
    }

    private static bool ShouldUseWireframeMaterial(string packageName)
    {
        return PlateauPackageCatalog.IsWireframeOverlayPackage(packageName);
    }

    private static bool ShouldUseBuildingWallSkin(DefaultMaterialRequest request)
    {
        return request.PreferUvProjection
            && PlateauPackageCatalog.IsBuildingPackage(request.PackageName)
            && request.SurfaceRole is DefaultMaterialSurfaceRole.Wall
                or DefaultMaterialSurfaceRole.Closure
                or DefaultMaterialSurfaceRole.Unknown;
    }

    private static string ResolveBundledTextureFamily(DefaultMaterialRequest request, bool useWallSkin)
    {
        if (useWallSkin)
        {
            return SelectWallSkinFamily(request);
        }

        if (PlateauPackageCatalog.IsBuildingPackage(request.PackageName))
        {
            return BundledDefaultMaterialFamilies.Roof;
        }

        if (PlateauPackageCatalog.IsRoadPackage(request.PackageName)
            || PlateauPackageCatalog.IsPathLikePackage(request.PackageName))
        {
            return BundledDefaultMaterialFamilies.Road;
        }

        if (PlateauPackageCatalog.IsVegetationPackage(request.PackageName))
        {
            return BundledDefaultMaterialFamilies.Vegetation;
        }

        if (PlateauPackageCatalog.IsCityFurniturePackage(request.PackageName))
        {
            return BundledDefaultMaterialFamilies.CityFurniture;
        }

        return BundledDefaultMaterialFamilies.Other;
    }

    private static string SelectWallSkinFamily(DefaultMaterialRequest request)
    {
        BuildingAttributeContext attributes = request.BuildingAttributes ?? BuildingAttributeContext.Empty;
        int? floorCount = request.FloorsAboveGround;
        double? heightMeters = GetEffectiveHeightMeters(request);
        double? footprintArea = request.FootprintAreaSquareMeters;
        bool lowRise = IsLowRise(floorCount, heightMeters);
        bool midOrHighRise = IsMidOrHighRise(floorCount, heightMeters);
        bool midrise = IsMidrise(floorCount, heightMeters);
        bool highrise = IsHighrise(floorCount, heightMeters);
        bool landmark = IsLandmarkScale(floorCount, heightMeters);
        bool largeLowRise = lowRise && footprintArea is >= 1000.0;

        if (landmark)
        {
            return HasNightOccupancy(attributes)
                ? BundledDefaultMaterialFamilies.FacadeHighriseNightLow
                : BundledDefaultMaterialFamilies.FacadeHighriseGlass;
        }

        if (highrise)
        {
            return HasNightOccupancy(attributes)
                ? BundledDefaultMaterialFamilies.FacadeHighriseNightLow
                : BundledDefaultMaterialFamilies.FacadeHighriseGlass;
        }

        if (midrise && IsFacadeLikeMidriseUse(attributes))
        {
            return BundledDefaultMaterialFamilies.FacadeMidriseGrid;
        }

        if (HasRawBuildingCode(attributes, "431")
            || HasUse(attributes, PlateauBuildingUse.Warehouse)
            || HasRawBuildingCode(attributes, "441")
            || HasUse(attributes, PlateauBuildingUse.Factory)
            || largeLowRise)
        {
            return BundledDefaultMaterialFamilies.WallFactoryMetal;
        }

        if (HasRawBuildingCode(attributes, "451"))
        {
            return BundledDefaultMaterialFamilies.WallWoodRural;
        }

        if (HasBrickLikeStructure(attributes))
        {
            return BundledDefaultMaterialFamilies.WallBrickRetro;
        }

        if (HasUse(attributes, PlateauBuildingUse.Commercial)
            || HasUse(attributes, PlateauBuildingUse.Office))
        {
            return lowRise
                ? BundledDefaultMaterialFamilies.WallCommercialPanel
                : BundledDefaultMaterialFamilies.WallRcPaintedMid;
        }

        if (HasUse(attributes, PlateauBuildingUse.Public)
            || HasUse(attributes, PlateauBuildingUse.Education))
        {
            return BundledDefaultMaterialFamilies.WallSchoolPublicBand;
        }

        if (HasUse(attributes, PlateauBuildingUse.Apartment))
        {
            return lowRise
                ? BundledDefaultMaterialFamilies.WallResidentialTileLow
                : BundledDefaultMaterialFamilies.WallApartmentTileMid;
        }

        if (HasUse(attributes, PlateauBuildingUse.MixedResidential))
        {
            return lowRise
                ? BundledDefaultMaterialFamilies.WallResidentialPlasterLow
                : BundledDefaultMaterialFamilies.WallApartmentTileMid;
        }

        if (HasUse(attributes, PlateauBuildingUse.DetachedResidential))
        {
            return IsWeightedAlternate(request.VariantSelectionKey)
                ? BundledDefaultMaterialFamilies.WallResidentialTileLow
                : BundledDefaultMaterialFamilies.WallResidentialPlasterLow;
        }

        if (IsRobustStructure(attributes) || midOrHighRise)
        {
            return BundledDefaultMaterialFamilies.WallRcPaintedMid;
        }

        return BundledDefaultMaterialFamilies.WallResidentialPlasterLow;
    }

    private static bool IsLowRise(int? floorCount, double? heightMeters)
    {
        return (!FacadeFloorMetrics.IsUsableFloorCount(floorCount) || floorCount <= 3)
            && (!heightMeters.HasValue || heightMeters.Value < 12.0);
    }

    private static bool IsMidOrHighRise(int? floorCount, double? heightMeters)
    {
        return (FacadeFloorMetrics.IsUsableFloorCount(floorCount) && floorCount >= 4)
            || heightMeters is >= 12.0;
    }

    private static bool IsMidrise(int? floorCount, double? heightMeters)
    {
        return (heightMeters is >= 25.0 and < 80.0)
            || (FacadeFloorMetrics.IsUsableFloorCount(floorCount) && floorCount is >= 8 and < 20);
    }

    private static bool IsHighrise(int? floorCount, double? heightMeters)
    {
        return (heightMeters is >= 80.0 and < 150.0)
            || (FacadeFloorMetrics.IsUsableFloorCount(floorCount) && floorCount is >= 20 and < 35);
    }

    private static bool IsLandmarkScale(int? floorCount, double? heightMeters)
    {
        return heightMeters is >= 150.0
            || (FacadeFloorMetrics.IsUsableFloorCount(floorCount) && floorCount >= 35);
    }

    private static double? GetEffectiveHeightMeters(DefaultMaterialRequest request)
    {
        return TryGetPositiveValue(request.MeasuredHeightMeters)
            ?? TryGetPositiveValue(request.GeometryHeightMeters);
    }

    private static double? TryGetPositiveValue(double? value)
    {
        return value is > 0.0 && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
            ? value.Value
            : null;
    }

    private static bool IsFacadeLikeMidriseUse(BuildingAttributeContext attributes)
    {
        return HasUse(attributes, PlateauBuildingUse.Office)
            || HasUse(attributes, PlateauBuildingUse.Commercial)
            || HasUse(attributes, PlateauBuildingUse.Public)
            || HasUse(attributes, PlateauBuildingUse.Education)
            || HasUse(attributes, PlateauBuildingUse.Apartment)
            || HasUse(attributes, PlateauBuildingUse.MixedResidential)
            || HasRawBuildingCode(attributes, "403");
    }

    private static bool HasNightOccupancy(BuildingAttributeContext attributes)
    {
        return HasUse(attributes, PlateauBuildingUse.Apartment)
            || HasUse(attributes, PlateauBuildingUse.MixedResidential)
            || HasRawBuildingCode(attributes, "403");
    }

    private static bool IsRobustStructure(BuildingAttributeContext attributes)
    {
        return attributes.Structures.Any(static structure => structure.Value is PlateauBuildingStructure.ReinforcedConcrete
            or PlateauBuildingStructure.SteelReinforcedConcrete
            or PlateauBuildingStructure.NonWood);
    }

    private static bool HasBrickLikeStructure(BuildingAttributeContext attributes)
    {
        return attributes.Structures.Any(static structure => structure.Value is PlateauBuildingStructure.ConcreteBlock);
    }

    private static bool HasUse(BuildingAttributeContext attributes, PlateauBuildingUse use)
    {
        return attributes.Uses.Any(candidate => candidate.Value == use)
            || attributes.DetailedUses.Any(candidate => candidate.Value == use)
            || attributes.CityGmlFunctionCodes.Any(code => HasRawBuildingCode(code, use));
    }

    private static bool HasRawBuildingCode(BuildingAttributeContext attributes, string code)
    {
        return attributes.CityGmlFunctionCodes.Any(candidate => IsSameBroadCode(candidate, code))
            || attributes.CityGmlClassCodes.Any(candidate => IsSameBroadCode(candidate, code));
    }

    private static bool HasRawBuildingCode(string code, PlateauBuildingUse use)
    {
        string broadCode = CreateBroadBuildingCode(code);
        return use switch
        {
            PlateauBuildingUse.DetachedResidential => broadCode is "411" or "111",
            PlateauBuildingUse.Apartment => broadCode is "412" or "112" or "113",
            PlateauBuildingUse.MixedResidential => broadCode is "413" or "414" or "415" or "114" or "115" or "116",
            PlateauBuildingUse.Office => broadCode is "401" or "131",
            PlateauBuildingUse.Commercial => broadCode is "402" or "403" or "404" or "151" or "152",
            PlateauBuildingUse.Warehouse => broadCode is "431" or "171" or "172",
            PlateauBuildingUse.Factory => broadCode is "441" or "174",
            PlateauBuildingUse.Education => broadCode is "422" or "181",
            PlateauBuildingUse.Public => broadCode is "421" or "191" or "192" or "193",
            _ => false,
        };
    }

    private static bool IsSameBroadCode(string candidate, string expected)
    {
        return string.Equals(CreateBroadBuildingCode(candidate), expected, StringComparison.Ordinal);
    }

    private static string CreateBroadBuildingCode(string code)
    {
        string trimmed = code.Trim();
        return trimmed.Length <= 3 ? trimmed : trimmed[..3];
    }

    private static bool IsWeightedAlternate(string variantSelectionKey)
    {
        return SelectStableBucket($"{variantSelectionKey}:residential-wall-weight", 5) == 0;
    }

    private static int SelectBundledVariantIndex(string family, string variantSelectionKey)
    {
        IReadOnlyList<string> variants = BundledDefaultMaterialFamilies.GetVariants(family);
        return SelectStableBucket(variantSelectionKey, variants.Count);
    }

    private static int SelectStableBucket(string variantSelectionKey, int bucketCount)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(variantSelectionKey);
        byte[] hashBytes = SHA256.HashData(keyBytes);
        int hashCode = BinaryPrimitives.ReadInt32LittleEndian(hashBytes) & int.MaxValue;
        return hashCode % bucketCount;
    }

    private static Float2 ToContractFloat2(Domain.Importing.ScalarPair value) => new(value.X, value.Y);
}
