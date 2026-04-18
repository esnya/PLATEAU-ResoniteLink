using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public interface IResoniteConstructionComposer
{
    IResoniteConstructionSource Compose(
        PlateauImportRequest request,
        LocalCityGmlDocumentSet documentSet,
        Action<string>? progressReporter = null);
}
