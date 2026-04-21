using System;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class CityGmlAppearanceStoreFactory : ICityGmlAppearanceStoreFactory
{
    public ICityGmlAppearanceStore Create(
        string sourceFileRelativePath,
        IPlateauDatasetContentSource datasetSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileRelativePath);
        ArgumentNullException.ThrowIfNull(datasetSource);
        return new CityGmlAppearanceStore(sourceFileRelativePath, datasetSource);
    }
}
