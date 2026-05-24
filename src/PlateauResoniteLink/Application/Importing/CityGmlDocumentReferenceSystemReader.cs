using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlDocumentReferenceSystemReader
{
    internal static readonly XNamespace Gml = "http://www.opengis.net/gml";

    internal static async Task<CoordinateReferenceSystem> ReadAsync(
        IPlateauDatasetContentSource datasetSource,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await datasetSource.OpenReadAsync(relativePath, cancellationToken);
        using XmlReader reader = XmlReader.Create(stream, CityGmlStreamingXmlReaderSettings.Create());

        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element
                || !string.Equals(reader.NamespaceURI, Gml.NamespaceName, StringComparison.Ordinal)
                || !string.Equals(reader.LocalName, "Envelope", StringComparison.Ordinal))
            {
                continue;
            }

            return CoordinateReferenceSystem.Parse(reader.GetAttribute("srsName"));
        }

        try
        {
            return CoordinateReferenceSystem.Parse((string?)null);
        }
        catch (PlateauImportValidationException)
        {
            throw new PlateauImportValidationException(
                [$"CityGML file '{NormalizePath(relativePath)}' does not declare a supported coordinate reference system."]);
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/');
    }
}
