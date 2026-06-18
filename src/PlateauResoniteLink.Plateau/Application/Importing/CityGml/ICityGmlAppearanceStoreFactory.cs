namespace PlateauResoniteLink.Plateau.Application.Importing.CityGml;

internal interface ICityGmlAppearanceStoreFactory
{
    ICityGmlAppearanceStore Create(
        string sourceFileRelativePath,
        IPlateauDatasetContentSource datasetSource);
}
