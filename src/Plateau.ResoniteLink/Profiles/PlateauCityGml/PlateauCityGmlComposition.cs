using Plateau.ResoniteLink.Application.Importing;

namespace Plateau.ResoniteLink.Profiles.PlateauCityGml;

public static class PlateauCityGmlComposition
{
    public static ICityGmlDocumentReader CreateDocumentReader(IPlateauDatasetContentSourceFactory datasetContentSourceFactory)
    {
        return PlateauCityGmlImportComposition.CreateDocumentReader(datasetContentSourceFactory);
    }

    public static IResoniteConstructionSourceFactory CreateConstructionSourceFactory(
        IPlateauDatasetContentSourceFactory datasetContentSourceFactory)
    {
        ICityGmlDocumentReader documentReader = CreateDocumentReader(datasetContentSourceFactory);
        IResoniteConstructionComposer constructionComposer = PlateauCityGmlImportComposition.CreateConstructionComposer(
            PlateauCityGmlImportComposition.CreateGeometryProjector(
                PlateauCityGmlImportComposition.CreateMaterialResolver()));
        return PlateauCityGmlImportComposition.CreateConstructionSourceFactory(documentReader, constructionComposer);
    }

    public static IResoniteConstructionSourceFactory CreateConstructionSourceFactory()
    {
        return CreateConstructionSourceFactory(
            new DefaultPlateauDatasetContentSourceFactory(
                new RemoteArchiveDistributionPolicy(),
                new ArchiveFileLayoutPolicy()));
    }
}
