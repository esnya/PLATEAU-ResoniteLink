using System.Text.RegularExpressions;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal static class MeshCodeInput
{
    public static bool IsLiteralMeshCode(string meshCode)
    {
        return PlateauMeshCode.TryGetBounds(meshCode, out _);
    }

    public static bool TryCreateRegex(string meshCode, out Regex? regex, out string? error)
    {
        regex = null;
        error = null;

        if (IsLiteralMeshCode(meshCode))
        {
            return true;
        }

        try
        {
            regex = new Regex(
                $@"\A(?:{meshCode})\z",
                RegexOptions.Compiled | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException exception)
        {
            error = $"The mesh code value '{meshCode}' is not a valid regular expression: {exception.Message}";
            return false;
        }
    }
}
