using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface INonDemCityObjectBakeCandidateFactory
{
    Task<NonDemCityObjectBakeCandidate?> CreateAsync(
        NonDemBufferedCityObject bufferedCityObject,
        CancellationToken cancellationToken);
}

internal sealed class NonDemCityObjectBakeCandidateFactory(
    INonDemBakeEntryFactory entryFactory) : INonDemCityObjectBakeCandidateFactory
{
    private readonly INonDemBakeEntryFactory entryFactory = entryFactory
        ?? throw new ArgumentNullException(nameof(entryFactory));

    public async Task<NonDemCityObjectBakeCandidate?> CreateAsync(
        NonDemBufferedCityObject bufferedCityObject,
        CancellationToken cancellationToken)
    {
        ResoniteConstructionCityObject cityObject = bufferedCityObject.CityObject;
        NonDemCityObjectBakePolicy policy = bufferedCityObject.Policy;
        if (!NonDemCityObjectBakeMaterialClassifier.TryCreateMaterialBySubmeshIndex(cityObject, out _))
        {
            throw new InvalidOperationException(
                $"Non-DEM bake city object '{cityObject.DisplayName}' contained duplicate material assignments for a submesh.");
        }

        ResoniteConstructionCityObject normalizedCityObject = ResoniteDynamicMaterialUvNormalizer.Normalize(cityObject);
        if (!NonDemCityObjectBakeMaterialClassifier.TryCreateMaterialBySubmeshIndex(normalizedCityObject, out Dictionary<int, ResoniteMaterialBinding>? materialBySubmeshIndex))
        {
            throw new InvalidOperationException(
                $"Non-DEM bake city object '{cityObject.DisplayName}' contained duplicate material assignments for a submesh.");
        }

        List<NonDemAtlasBatchEntry> atlasEntries = [];
        List<NonDemPreservedSubmeshEntry> preservedEntries = [];
        bool hadAtlasCandidateMaterial = false;
        foreach (ResoniteMeshSubmesh submesh in normalizedCityObject.Mesh.Submeshes.OrderBy(static candidate => candidate.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!materialBySubmeshIndex.TryGetValue(submesh.Index, out ResoniteMaterialBinding? material))
            {
                throw new InvalidOperationException(
                    $"Non-DEM bake city object '{cityObject.DisplayName}' left submesh index {submesh.Index} without a material assignment.");
            }

            NonDemMaterialBakeCategory category = NonDemCityObjectBakeMaterialClassifier.Classify(material);
            switch (category)
            {
                case NonDemMaterialBakeCategory.AtlasCandidate:
                    hadAtlasCandidateMaterial = true;
                    NonDemBakeEntry bakeEntry = await entryFactory.CreateAsync(
                        normalizedCityObject,
                        submesh,
                        material,
                        cancellationToken);
                    switch (bakeEntry)
                    {
                        case NonDemBakeEntry.Atlas atlas:
                            atlasEntries.Add(atlas.Entry);
                            break;
                        case NonDemBakeEntry.Preserved preserved:
                            preservedEntries.Add(preserved.Entry);
                            break;
                    }

                    break;
                case NonDemMaterialBakeCategory.PreservedCommonMaterial when policy.PreserveCommonMaterials:
                case NonDemMaterialBakeCategory.PreservedTextureless when policy.PreserveTexturelessMaterials:
                case NonDemMaterialBakeCategory.PreservedVertexColor when policy.PreserveVertexColorMaterials:
                case NonDemMaterialBakeCategory.PreservedOther:
                    ResoniteMeshSubmesh normalizedSubmesh = normalizedCityObject.Mesh.Submeshes.Single(candidate => candidate.Index == submesh.Index);
                    ResoniteMaterialBinding normalizedMaterial = normalizedCityObject.Materials.Single(candidate => candidate.SubmeshIndices.Contains(submesh.Index));
                    preservedEntries.Add(new NonDemPreservedSubmeshEntry(normalizedCityObject, normalizedSubmesh, normalizedMaterial));
                    break;
            }
        }

        if (policy.RequireAtlasCandidateMaterial && !hadAtlasCandidateMaterial)
        {
            return null;
        }

        if (atlasEntries.Count == 0 && preservedEntries.Count == 0)
        {
            throw new InvalidOperationException(
                $"Non-DEM bake city object '{cityObject.DisplayName}' produced no atlas or preserved submesh candidate.");
        }

        return new NonDemCityObjectBakeCandidate(normalizedCityObject, atlasEntries, preservedEntries);
    }
}
