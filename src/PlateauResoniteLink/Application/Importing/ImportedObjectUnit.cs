using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Application.Importing;

public sealed record ImportedObjectUnitDescriptor(
    string ScopeKey,
    string ScopePath,
    string PackageName,
    int? LodLevel,
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
        string scopeKey,
        string scopePath,
        string packageName,
        int? lodLevel,
        IReadOnlyList<ImportedCityObject> cityObjects,
        string? matchedMeshCode = null)
        : this(
            new ImportedObjectUnitDescriptor(
                scopeKey,
                scopePath,
                packageName,
                lodLevel,
                matchedMeshCode),
            cityObjects)
    {
    }
}
