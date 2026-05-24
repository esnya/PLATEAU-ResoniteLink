using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlAppearanceStreamingProbe
{
    internal static readonly XNamespace App = "http://www.opengis.net/citygml/appearance/2.0";
    internal static readonly XNamespace Core = "http://www.opengis.net/citygml/2.0";

    internal static async Task<bool> MayContainAppearanceMembersAsync(
        IPlateauDatasetContentSource datasetSource,
        string relativePath,
        CancellationToken cancellationToken)
    {
        const int ProbeByteCount = 4096;

        await using Stream stream = await datasetSource.OpenReadAsync(relativePath, cancellationToken);
        byte[] buffer = new byte[ProbeByteCount];
        int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, ProbeByteCount), cancellationToken);
        if (bytesRead <= 0)
        {
            return false;
        }

        ReadOnlySpan<byte> probe = buffer.AsSpan(0, bytesRead);
        return probe.IndexOf("xmlns:app="u8) >= 0 || probe.IndexOf("<app:"u8) >= 0;
    }

    internal static async Task<bool> HasLateAppearanceMembersAfterCityObjectAsync(
        IPlateauDatasetContentSource datasetSource,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await datasetSource.OpenReadAsync(relativePath, cancellationToken);
        using XmlReader reader = XmlReader.Create(stream, CityGmlStreamingXmlReaderSettings.Create());

        bool hasCityObject = false;
        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (string.Equals(reader.NamespaceURI, Core.NamespaceName, StringComparison.Ordinal)
                && string.Equals(reader.LocalName, "cityObjectMember", StringComparison.Ordinal))
            {
                hasCityObject = true;
                continue;
            }

            if (!hasCityObject || !string.Equals(reader.NamespaceURI, App.NamespaceName, StringComparison.Ordinal))
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
