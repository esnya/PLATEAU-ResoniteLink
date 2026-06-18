using System;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal sealed class ResoniteMeshValidationException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
