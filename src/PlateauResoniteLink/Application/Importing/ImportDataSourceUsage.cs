namespace PlateauResoniteLink.Application.Importing;

public enum ImportDataSourceCategory
{
    CityGmlSourceFile,
    DemTextureSource,
}

public sealed record ImportDataSourceUsage(
    ImportDataSourceCategory Category,
    string Description,
    int UsedCount);
