using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class DefaultMaterialResolver : IDefaultMaterialResolver
{
    private readonly DefaultMaterialMemberSelector memberSelector;

    public DefaultMaterialResolver(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials)
    {
        memberSelector = new DefaultMaterialMemberSelector(commonMaterials);
    }

    public ResolvedMaterial ResolveMaterial(DefaultMaterialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ResolveMaterialCore(request, memberSelector);
    }

    internal static ResolvedMaterial ResolveMaterialCore(
        DefaultMaterialRequest request,
        DefaultMaterialMemberSelector memberSelector)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(memberSelector);

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

        DefaultCommonMaterialMember commonMaterial = memberSelector.Select(request);
        string family = commonMaterial.Family
            ?? throw new InvalidOperationException("Selected default material member is not a bundled material.");
        int bundledVariantIndex = commonMaterial.BundledVariantIndex
            ?? throw new InvalidOperationException("Selected bundled material member does not expose a variant index.");
        BundledDefaultMaterialVariant variant = commonMaterial.BundledVariant
            ?? throw new InvalidOperationException("Selected bundled material member does not expose a variant.");
        BundledDefaultMaterialProfile uvProfile = variant.TextureSet;
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
            TextureOffset: textureOffset,
            CommonMaterial: commonMaterial);
    }

    private static bool ShouldUseWireframeMaterial(string packageName)
    {
        return PlateauPackageCatalog.IsWireframeOverlayPackage(packageName);
    }

    private static Float2 ToContractFloat2(Domain.Importing.ScalarPair value) => new(value.X, value.Y);
}
