using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Application.Importing;

public sealed record ImportedObjectUnitDescriptor(
    string SourceFileRelativePath,
    string PackageName,
    DetailEntry DetailEntry,
    DetailEntry FinestDetailGroup,
    string? MatchedMeshCode = null);

public sealed record ImportedObjectUnit
{
    public ImportedObjectUnit(
        ImportedObjectUnitDescriptor descriptor,
        IReadOnlyList<ImportedCityObject> cityObjects)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(cityObjects);
        if (cityObjects.Count == 0)
        {
            throw new ArgumentException("Imported object units must contain at least one city object.", nameof(cityObjects));
        }

        Descriptor = descriptor;
        CityObjects = cityObjects;
    }

    public ImportedObjectUnitDescriptor Descriptor { get; init; }

    public IReadOnlyList<ImportedCityObject> CityObjects { get; init; }

    public ImportedObjectUnit(
        string sourceFileRelativePath,
        string packageName,
        DetailEntry detailEntry,
        DetailEntry finestDetailGroup,
        IReadOnlyList<ImportedCityObject> cityObjects,
        string? matchedMeshCode = null)
        : this(
            new ImportedObjectUnitDescriptor(
                sourceFileRelativePath,
                packageName,
                detailEntry,
                finestDetailGroup,
                matchedMeshCode),
            cityObjects)
    {
    }

    public ImportedObjectUnit(
        string sourceFileRelativePath,
        string packageName,
        int? sourceRepresentationIndex,
        IReadOnlyList<ImportedCityObject> cityObjects,
        string? matchedMeshCode = null)
        : this(
            sourceFileRelativePath,
            packageName,
            DetailEntry.FromSourceRepresentationIndex(sourceRepresentationIndex),
            DetailEntry.FromSourceRepresentationIndex(sourceRepresentationIndex),
            cityObjects,
            matchedMeshCode)
    {
    }
}
