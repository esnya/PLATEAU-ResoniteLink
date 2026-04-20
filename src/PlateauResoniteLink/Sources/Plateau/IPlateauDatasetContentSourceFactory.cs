namespace PlateauResoniteLink.Application.Importing;

public interface IPlateauDatasetContentSourceFactory
{
    Task<IPlateauDatasetContentSource> CreateAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}
