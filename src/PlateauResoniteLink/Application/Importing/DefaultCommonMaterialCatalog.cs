using System;
using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1822:Mark members as static",
    Justification = "The catalog intentionally stays instance-based so material selection can remain a replaceable service seam.")]
public sealed class DefaultCommonMaterialCatalog
{
    public CommonMaterialCatalog<DefaultCommonMaterialMember> Create()
    {
        SortedSet<string> families = new(StringComparer.Ordinal);
        AddBuildingFamilies(families);
        families.Add(BundledDefaultMaterialFamilies.RoadUv);
        families.Add(BundledDefaultMaterialFamilies.RoadTriplanar);
        families.Add(BundledDefaultMaterialFamilies.Vegetation);
        families.Add(BundledDefaultMaterialFamilies.CityFurniture);
        families.Add(BundledDefaultMaterialFamilies.Other);

        List<DefaultCommonMaterialMember> materials = [];
        foreach (string family in families)
        {
            int variantCount = BundledDefaultMaterialFamilies.GetVariantDefinitions(family).Count;
            for (int variantIndex = 0; variantIndex < variantCount; variantIndex++)
            {
                materials.Add(DefaultCommonMaterialMember.Bundled(family, variantIndex));
            }
        }

        materials.AddRange(CreateSharedAlbedoCommonMaterialBindings());
        materials.AddRange(CreateSharedVertexColorCommonMaterialBindings());

        return new CommonMaterialCatalog<DefaultCommonMaterialMember>(materials, RejectDuplicateDefinitions);
    }

    private static void RejectDuplicateDefinitions(IReadOnlyList<DefaultCommonMaterialMember> materials)
    {
        HashSet<DefaultCommonMaterialMember> definitions = [];
        foreach (DefaultCommonMaterialMember material in materials)
        {
            if (!definitions.Add(material))
            {
                throw new InvalidOperationException("Common material catalog contains duplicate material definitions.");
            }
        }
    }

    private static void AddBuildingFamilies(SortedSet<string> families)
    {
        families.Add(BundledDefaultMaterialFamilies.Roof);
        foreach (string family in BundledDefaultMaterialFamilies.BuildingFacadeFamilies)
        {
            families.Add(family);
        }
    }

    private static IReadOnlyList<DefaultCommonMaterialMember> CreateSharedAlbedoCommonMaterialBindings()
    {
        return
        [
            CreateSharedAlbedoCommonMaterialBinding(depthOffset: null),
            CreateSharedAlbedoCommonMaterialBinding(LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset),
        ];
    }

    private static DefaultCommonMaterialMember CreateSharedAlbedoCommonMaterialBinding(MaterialDepthOffset? depthOffset)
    {
        return DefaultCommonMaterialMember.GenericUv(depthOffset);
    }

    private static IReadOnlyList<DefaultCommonMaterialMember> CreateSharedVertexColorCommonMaterialBindings()
    {
        return
        [
            CreateSharedVertexColorCommonMaterialBinding(depthOffset: null),
            CreateSharedVertexColorCommonMaterialBinding(LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset),
        ];
    }

    private static DefaultCommonMaterialMember CreateSharedVertexColorCommonMaterialBinding(MaterialDepthOffset? depthOffset)
    {
        return DefaultCommonMaterialMember.VertexColorUv(depthOffset);
    }
}
