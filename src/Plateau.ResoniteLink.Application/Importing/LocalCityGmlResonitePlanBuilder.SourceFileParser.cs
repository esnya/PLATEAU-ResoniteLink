using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;

using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public static partial class LocalCityGmlResonitePlanBuilder
{
    internal static ResoniteLocalOrigin? ResolveLocalOrigin(
        MeshCodeArea? requestedMeshArea)
    {
        return requestedMeshArea?.GetCenter();
    }

    internal static async Task<LocalCityGmlDocumentSet> ReadDocumentSetAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return await LocalCityGmlBootstrapPipeline.ReadDocumentSetCoreAsync(
            request,
            progressReporter,
            cancellationToken);
    }

    internal static Task<global::Plateau.ResoniteLink.Application.Importing.SourceFilePipeline[]> CreateSourceFilePipelinesCoreAsync(
        IReadOnlyList<global::Plateau.ResoniteLink.Application.Importing.SourceFileDescriptor> sourceFiles,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeArea> requestedMeshAreas,
        Action<string>? progressReporter,
        LodFilteringStrategy lodFilteringStrategy,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            sourceFiles
                .Select(sourceFile =>
                    new global::Plateau.ResoniteLink.Application.Importing.SourceFilePipeline(
                        sourceFile,
                        () => ParseSourceFileCoreAsync(
                            sourceFile,
                            datasetSource,
                            requestedMeshAreas,
                            progressReporter,
                            lodFilteringStrategy,
                            cancellationToken),
                        streamFactory: cancellationToken => StreamBootstrapParsedCityObjectsCoreAsync(
                            sourceFile,
                            datasetSource,
                            requestedMeshAreas,
                            lodFilteringStrategy,
                            cancellationToken)))
                .ToArray());
    }

    internal static async Task<global::Plateau.ResoniteLink.Application.Importing.ParsedSourceFileResult> ParseSourceFileCoreAsync(
        global::Plateau.ResoniteLink.Application.Importing.SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeArea> requestedMeshAreas,
        Action<string>? progressReporter,
        LodFilteringStrategy lodFilteringStrategy,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Stopwatch fileStopwatch = Stopwatch.StartNew();
        List<ParsedCityObject> cityObjects = [];
        CoordinateReferenceSystem? coordinateReferenceSystem = null;
        await using Stream stream = await datasetSource.OpenReadAsync(sourceFile.RelativePath, cancellationToken);
        await foreach (ParsedCityObject cityObject in StreamParsedCityObjectsFromStreamCoreAsync(
                           stream,
                           sourceFile.ToLegacy(),
                           datasetSource,
                           requestedMeshAreas,
                           lodFilteringStrategy,
                           parsedReferenceSystem => coordinateReferenceSystem ??= parsedReferenceSystem,
                           cancellationToken))
        {
            cityObjects.Add(cityObject);
        }

        fileStopwatch.Stop();

        ParsedCityObject[] cityObjectArray = cityObjects
            .OrderBy(static cityObject => cityObject.SlotKey, StringComparer.Ordinal)
            .ToArray();
        coordinateReferenceSystem ??= await ReadDocumentReferenceSystemCoreAsync(
            datasetSource,
            sourceFile.RelativePath,
            cancellationToken);
        global::Plateau.ResoniteLink.Application.Importing.TerrainHeightTriangle[] terrainTriangles = string.Equals(sourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            ? LocalCityGmlDemBootstrapSupport.CreateTerrainHeightTriangles(cityObjectArray.Select(BootstrapParsedCityObject.FromLegacy))
            : [];

        progressReporter?.Invoke(
            PlateauLog.Debug(
                "import",
                $"Parsed file '{sourceFile.RelativePath}' "
                + $"({sourceFile.PackageName}, {cityObjectArray.Length} city objects) "
                + $"in {fileStopwatch.Elapsed.TotalSeconds:F3}s."));

        return new global::Plateau.ResoniteLink.Application.Importing.ParsedSourceFileResult(
            sourceFile,
            cityObjectArray.Select(BootstrapParsedCityObject.FromLegacy).ToArray(),
            global::Plateau.ResoniteLink.Application.Importing.CoordinateReferenceSystem.FromLegacy(coordinateReferenceSystem),
            terrainTriangles,
            fileStopwatch.Elapsed);
    }

    private static async IAsyncEnumerable<global::Plateau.ResoniteLink.Application.Importing.BootstrapParsedCityObject> StreamBootstrapParsedCityObjectsCoreAsync(
        global::Plateau.ResoniteLink.Application.Importing.SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeArea> requestedMeshAreas,
        LodFilteringStrategy? lodFilteringStrategy,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (ParsedCityObject cityObject in StreamParsedCityObjectsCoreAsync(
                           sourceFile.ToLegacy(),
                           datasetSource,
                           requestedMeshAreas,
                           lodFilteringStrategy,
                           cancellationToken))
        {
            yield return global::Plateau.ResoniteLink.Application.Importing.BootstrapParsedCityObject.FromLegacy(cityObject);
        }
    }

    internal static async IAsyncEnumerable<ParsedCityObject> StreamParsedCityObjectsCoreAsync(
        SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeArea> requestedMeshAreas,
        LodFilteringStrategy? lodFilteringStrategy,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        lodFilteringStrategy ??= new LodFilteringStrategy();
        await using Stream stream = await datasetSource.OpenReadAsync(sourceFile.RelativePath, cancellationToken);
        await foreach (ParsedCityObject cityObject in StreamParsedCityObjectsFromStreamCoreAsync(
                           stream,
            sourceFile,
            datasetSource,
            requestedMeshAreas,
            lodFilteringStrategy,
            parsedReferenceSystem: null,
            cancellationToken))
        {
            yield return cityObject;
        }
    }

    internal static async Task<CoordinateReferenceSystem> ReadDocumentReferenceSystemCoreAsync(
        IPlateauDatasetContentSource datasetSource,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await datasetSource.OpenReadAsync(relativePath, cancellationToken);
        XmlReaderSettings settings = CreateStreamingReaderSettings();
        using XmlReader reader = XmlReader.Create(stream, settings);

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

    private static async IAsyncEnumerable<ParsedCityObject> StreamParsedCityObjectsFromStreamCoreAsync(
        Stream stream,
        SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeArea> requestedMeshAreas,
        LodFilteringStrategy? lodFilteringStrategy,
        Action<CoordinateReferenceSystem>? parsedReferenceSystem,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        lodFilteringStrategy ??= new LodFilteringStrategy();
        Dictionary<string, ResoniteColor> colorsByPolygonId = new(StringComparer.Ordinal);
        Dictionary<string, TextureAssignment> texturesByPolygonId = new(StringComparer.Ordinal);
        AppearanceLibrary appearanceLibrary = new(colorsByPolygonId, texturesByPolygonId);
        CoordinateReferenceSystem coordinateReferenceSystem = CoordinateReferenceSystem.Parse((string?)null);
        bool emittedCityObject = false;

        using XmlReader reader = XmlReader.Create(stream, CreateStreamingReaderSettings());
        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (string.Equals(reader.NamespaceURI, Gml.NamespaceName, StringComparison.Ordinal)
                && string.Equals(reader.LocalName, "Envelope", StringComparison.Ordinal))
            {
                coordinateReferenceSystem = CoordinateReferenceSystem.Parse(reader.GetAttribute("srsName"));
                parsedReferenceSystem?.Invoke(coordinateReferenceSystem);
                continue;
            }

            if (!string.Equals(reader.NamespaceURI, App.NamespaceName, StringComparison.Ordinal)
                && !string.Equals(reader.NamespaceURI, Core.NamespaceName, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(reader.NamespaceURI, App.NamespaceName, StringComparison.Ordinal))
            {
                ThrowIfAppearanceDeclaredAfterCityObject(sourceFile.RelativePath, emittedCityObject);

                switch (reader.LocalName)
                {
                    case "ParameterizedTexture":
                        using (XmlReader textureSubtreeReader = reader.ReadSubtree())
                        {
                            XElement textureElement = await XElement.LoadAsync(textureSubtreeReader, LoadOptions.None, cancellationToken);
                            AppearanceLibrary.ParseParameterizedTexture(
                                textureElement,
                                sourceFile.RelativePath,
                                datasetSource,
                                texturesByPolygonId);
                        }

                        continue;
                    case "X3DMaterial":
                        using (XmlReader materialSubtreeReader = reader.ReadSubtree())
                        {
                            XElement materialElement = await XElement.LoadAsync(materialSubtreeReader, LoadOptions.None, cancellationToken);
                            AppearanceLibrary.ParseX3DMaterial(materialElement, colorsByPolygonId);
                        }

                        continue;
                }
            }

            if (!string.Equals(reader.NamespaceURI, Core.NamespaceName, StringComparison.Ordinal)
                || !string.Equals(reader.LocalName, "cityObjectMember", StringComparison.Ordinal))
            {
                continue;
            }

            using XmlReader subtreeReader = reader.ReadSubtree();
            XElement cityObjectMember = await XElement.LoadAsync(subtreeReader, LoadOptions.None, cancellationToken);
            XElement? cityObjectElement = cityObjectMember.Elements().FirstOrDefault();
            if (cityObjectElement is null)
            {
                continue;
            }

            ParsedCityObject? cityObject = ParseCityObject(
                cityObjectElement,
                sourceFile.PackageName,
                sourceFile.RelativePath,
                sourceFile.MatchedMeshCode,
                sourceFile.RequiresMeshAreaFilter,
                appearanceLibrary,
                coordinateReferenceSystem,
                sourceFile.RequiresMeshAreaFilter ? requestedMeshAreas : null,
                lodFilteringStrategy);
            if (cityObject is null)
            {
                emittedCityObject = true;
                continue;
            }

            emittedCityObject = true;
            yield return cityObject;
        }
    }

    private static XmlReaderSettings CreateStreamingReaderSettings()
    {
        return new XmlReaderSettings
        {
            Async = true,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Ignore,
        };
    }

    private static void ThrowIfAppearanceDeclaredAfterCityObject(string relativePath, bool emittedCityObject)
    {
        if (!emittedCityObject)
        {
            return;
        }

        throw new PlateauImportValidationException(
            [$"CityGML file '{NormalizePath(relativePath)}' declares appearance members after cityObjectMember, which is not supported by the streaming parser."]);
    }
}
