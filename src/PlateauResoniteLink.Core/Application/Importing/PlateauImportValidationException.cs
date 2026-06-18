using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Core.Application.Importing;

public sealed class PlateauImportValidationException(IReadOnlyList<string> errors)
    : Exception("The PLATEAU import request is invalid.")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
