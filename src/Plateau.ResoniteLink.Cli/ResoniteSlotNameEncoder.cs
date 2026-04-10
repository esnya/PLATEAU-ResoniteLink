using System.Text;

namespace Plateau.ResoniteLink.Cli;

internal static class ResoniteSlotNameEncoder
{
    public static string Encode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Convert.ToHexString(Encoding.UTF8.GetBytes(value)).ToLowerInvariant();
    }
}
