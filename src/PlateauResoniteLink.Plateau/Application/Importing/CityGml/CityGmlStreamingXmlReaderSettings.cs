using System.Xml;

namespace PlateauResoniteLink.Plateau.Application.Importing.CityGml;

internal static class CityGmlStreamingXmlReaderSettings
{
    internal static XmlReaderSettings Create()
    {
        return new XmlReaderSettings
        {
            Async = true,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Ignore,
        };
    }
}
