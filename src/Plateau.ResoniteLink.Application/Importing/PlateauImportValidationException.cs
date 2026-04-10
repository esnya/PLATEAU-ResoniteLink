using System.Collections.ObjectModel;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class PlateauImportValidationException : Exception
{
    public PlateauImportValidationException()
        : this("The PLATEAU import request is invalid.")
    {
    }

    public PlateauImportValidationException(string message)
        : this(message, innerException: null)
    {
    }

    public PlateauImportValidationException(string message, Exception? innerException)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(message);
        Errors = Array.Empty<string>();
    }

    public PlateauImportValidationException(IReadOnlyList<string> errors)
        : this("The PLATEAU import request is invalid.", errors)
    {
    }

    public PlateauImportValidationException(string message, IReadOnlyList<string> errors, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(errors);

        for (int index = 0; index < errors.Count; index++)
        {
            ArgumentNullException.ThrowIfNull(errors[index], $"{nameof(errors)}[{index}]");
        }

        Errors = errors.Count == 0
            ? Array.Empty<string>()
            : new ReadOnlyCollection<string>([.. errors]);
    }

    public IReadOnlyList<string> Errors { get; }
}
