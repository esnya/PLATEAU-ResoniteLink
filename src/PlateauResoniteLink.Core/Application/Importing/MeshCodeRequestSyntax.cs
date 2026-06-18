using System;
using System.Linq;

using System.Text.RegularExpressions;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public static class MeshCodeRequestSyntax
{
    public static bool IsLiteralMeshCodeRequest(string meshCodeRequest)
    {
        return PlateauMeshCode.TryGetBounds(meshCodeRequest, out _);
    }

    public static bool TryCreateSelectionRegex(string meshCodeRequest, out Regex? regex, out string? error)
    {
        regex = null;
        error = null;

        if (IsLiteralMeshCodeRequest(meshCodeRequest))
        {
            return true;
        }

        if (meshCodeRequest.All(char.IsDigit))
        {
            error = $"The mesh-code value '{meshCodeRequest}' is not a supported literal mesh-code.";
            return false;
        }

        try
        {
            regex = new Regex(
                $@"\A(?:{meshCodeRequest})\z",
                RegexOptions.Compiled | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException exception)
        {
            error = $"The mesh-code value '{meshCodeRequest}' is not a valid regular expression: {exception.Message}";
            return false;
        }
    }
}
