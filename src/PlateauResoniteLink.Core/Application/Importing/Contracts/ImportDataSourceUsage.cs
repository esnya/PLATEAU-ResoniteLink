namespace PlateauResoniteLink.Core.Application.Importing.Contracts;

public enum ImportDataSourceCategory
{
    CityGmlSourceFile,
    DemTextureSource,
}

public sealed record ImportDataSourceUsage(
    ImportDataSourceCategory Category,
    string Description,
    int UsedCount);
