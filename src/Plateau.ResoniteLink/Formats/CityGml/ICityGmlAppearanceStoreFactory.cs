namespace Plateau.ResoniteLink.Application.Importing;

internal interface ICityGmlAppearanceStoreFactory
{
    ICityGmlAppearanceStore Create(
        string sourceFileRelativePath,
        IPlateauDatasetContentSource datasetSource);
}
