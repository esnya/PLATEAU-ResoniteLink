using Plateau.ResoniteLink.Domain.Importing;
using Plateau.ResoniteLink.Profiles.PlateauCityGml;

namespace Plateau.ResoniteLink.Application.Importing;

public static class PlateauCityGmlConstructionSources
{
    internal static Func<IResoniteConstructionSourceFactory> FactoryProvider { get; set; } = PlateauCityGmlComposition.CreateConstructionSourceFactory;

    public static Task<IResoniteConstructionSource> CreateAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return FactoryProvider().CreateAsync(
            request,
            progressReporter,
            cancellationToken);
    }

    public static IResoniteConstructionSource Create(
        PlateauImportRequest request,
        Action<string>? progressReporter = null)
    {
        return CreateAsync(request, progressReporter).GetAwaiter().GetResult();
    }
}
