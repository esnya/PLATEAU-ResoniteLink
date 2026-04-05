using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

public sealed record BuildCommandOptions(
    PlateauImportRequest Request,
    string OutputRoot,
    Uri? ResoniteLinkUri);
