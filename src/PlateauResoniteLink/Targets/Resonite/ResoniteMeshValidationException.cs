namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteMeshValidationException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
