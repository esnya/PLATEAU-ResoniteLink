using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class LocalCityGmlSourceFileParser
{
    internal static GeodeticCoordinate? ResolveGeodeticCenter(
        MeshCodeBounds? requestedMeshArea)
    {
        return requestedMeshArea?.GetGeodeticCenter();
    }

    internal static Task<global::PlateauResoniteLink.Application.Importing.SourceFilePipeline[]> CreateSourceFilePipelinesCoreAsync(
        IReadOnlyList<global::PlateauResoniteLink.Application.Importing.SourceFileDescriptor> sourceFiles,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        Action<string>? progressReporter,
        LodFilteringStrategy lodFilteringStrategy,
        ICityGmlAppearanceStoreFactory appearanceStoreFactory,
        ICityGmlLodSelector lodSelector,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            sourceFiles
                .Select(sourceFile =>
                    new global::PlateauResoniteLink.Application.Importing.SourceFilePipeline(
                        sourceFile,
                        () => ParseSourceFileCoreAsync(
                            sourceFile,
                            datasetSource,
                            requestedMeshAreas,
                            progressReporter,
                            lodFilteringStrategy,
                            appearanceStoreFactory,
                            lodSelector,
                            cancellationToken),
                        streamFactory: cancellationToken => StreamParsedCityObjectsCoreAsync(
                            sourceFile,
                            datasetSource,
                            requestedMeshAreas,
                            lodFilteringStrategy,
                            appearanceStoreFactory,
                            lodSelector,
                            cancellationToken)))
                .ToArray());
    }

    internal static async Task<global::PlateauResoniteLink.Application.Importing.ParsedSourceFileResult> ParseSourceFileCoreAsync(
        global::PlateauResoniteLink.Application.Importing.SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        Action<string>? progressReporter,
        LodFilteringStrategy lodFilteringStrategy,
        ICityGmlAppearanceStoreFactory appearanceStoreFactory,
        ICityGmlLodSelector lodSelector,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Stopwatch fileStopwatch = Stopwatch.StartNew();
        List<global::PlateauResoniteLink.Application.Importing.ParsedCityObject> cityObjects = [];
        LocalCityGmlObjectProjection.CoordinateReferenceSystem? coordinateReferenceSystem = null;
        await foreach (global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject in StreamParsedCityObjectsCoreAsync(
                           sourceFile,
                           datasetSource,
                           requestedMeshAreas,
                           lodFilteringStrategy,
                           appearanceStoreFactory,
                           lodSelector,
                           parsedReferenceSystem: parsedReferenceSystem => coordinateReferenceSystem ??= parsedReferenceSystem,
                           cancellationToken))
        {
            cityObjects.Add(cityObject);
        }

        fileStopwatch.Stop();

        global::PlateauResoniteLink.Application.Importing.ParsedCityObject[] cityObjectArray = cityObjects
            .OrderBy(static cityObject => cityObject.SlotKey, StringComparer.Ordinal)
            .ToArray();
        coordinateReferenceSystem ??= await CityGmlDocumentReferenceSystemReader.ReadAsync(
            datasetSource,
            sourceFile.RelativePath,
            cancellationToken);
        global::PlateauResoniteLink.Application.Importing.TerrainHeightTriangle[] terrainTriangles = string.Equals(sourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            ? DemSourceDiscoverySupport.CreateTerrainHeightTriangles(cityObjectArray)
            : [];

        progressReporter?.Invoke(
            PlateauLog.Debug(
                "import",
                $"Parsed file '{sourceFile.RelativePath}' "
                + $"({sourceFile.PackageName}, {cityObjectArray.Length} city objects) "
                + $"in {fileStopwatch.Elapsed.TotalSeconds:F3}s."));

        return new global::PlateauResoniteLink.Application.Importing.ParsedSourceFileResult(
            sourceFile,
            cityObjectArray,
            coordinateReferenceSystem is null ? null : global::PlateauResoniteLink.Application.Importing.CoordinateReferenceSystem.FromProjectionModel(coordinateReferenceSystem),
            terrainTriangles,
            fileStopwatch.Elapsed);
    }

    private static async IAsyncEnumerable<global::PlateauResoniteLink.Application.Importing.ParsedCityObject> StreamParsedCityObjectsCoreAsync(
        global::PlateauResoniteLink.Application.Importing.SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        LodFilteringStrategy? lodFilteringStrategy,
        ICityGmlAppearanceStoreFactory appearanceStoreFactory,
        ICityGmlLodSelector lodSelector,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject in StreamParsedCityObjectsCoreAsync(
                           sourceFile,
                           datasetSource,
                           requestedMeshAreas,
                           lodFilteringStrategy,
                           appearanceStoreFactory,
                           lodSelector,
                           parsedReferenceSystem: null,
                           cancellationToken))
        {
            yield return cityObject;
        }
    }

    internal static async IAsyncEnumerable<global::PlateauResoniteLink.Application.Importing.ParsedCityObject> StreamParsedCityObjectsCoreAsync(
        global::PlateauResoniteLink.Application.Importing.SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        LodFilteringStrategy? lodFilteringStrategy,
        ICityGmlAppearanceStoreFactory appearanceStoreFactory,
        ICityGmlLodSelector lodSelector,
        Action<LocalCityGmlObjectProjection.CoordinateReferenceSystem>? parsedReferenceSystem = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        lodFilteringStrategy ??= new LodFilteringStrategy();
        if (await CityGmlAppearanceStreamingProbe.MayContainAppearanceMembersAsync(datasetSource, sourceFile.RelativePath, cancellationToken)
            && await CityGmlAppearanceStreamingProbe.HasLateAppearanceMembersAfterCityObjectAsync(datasetSource, sourceFile.RelativePath, cancellationToken))
        {
            await foreach (global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject in StreamParsedCityObjectsFromDocumentCoreAsync(
                               sourceFile,
                               datasetSource,
                               requestedMeshAreas,
                               lodFilteringStrategy,
                               appearanceStoreFactory,
                               lodSelector,
                               parsedReferenceSystem,
                               cancellationToken))
            {
                yield return cityObject;
            }

            yield break;
        }

        await using Stream stream = await datasetSource.OpenReadAsync(sourceFile.RelativePath, cancellationToken);
        await foreach (global::PlateauResoniteLink.Application.Importing.ParsedCityObject cityObject in StreamParsedCityObjectsFromStreamCoreAsync(
                           stream,
                           sourceFile,
                           datasetSource,
                           requestedMeshAreas,
                           lodFilteringStrategy,
                           appearanceStoreFactory,
                           lodSelector,
                           parsedReferenceSystem,
                           cancellationToken))
        {
            yield return cityObject;
        }
    }

    private static async IAsyncEnumerable<global::PlateauResoniteLink.Application.Importing.ParsedCityObject> StreamParsedCityObjectsFromStreamCoreAsync(
        Stream stream,
        global::PlateauResoniteLink.Application.Importing.SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        LodFilteringStrategy? lodFilteringStrategy,
        ICityGmlAppearanceStoreFactory appearanceStoreFactory,
        ICityGmlLodSelector lodSelector,
        Action<LocalCityGmlObjectProjection.CoordinateReferenceSystem>? parsedReferenceSystem,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        lodFilteringStrategy ??= new LodFilteringStrategy();
        ICityGmlAppearanceStore appearanceStore = appearanceStoreFactory.Create(sourceFile.RelativePath, datasetSource);
        LocalCityGmlObjectProjection.CoordinateReferenceSystem coordinateReferenceSystem =
            LocalCityGmlObjectProjection.CoordinateReferenceSystem.Parse((string?)null);

        using XmlReader reader = XmlReader.Create(stream, CityGmlStreamingXmlReaderSettings.Create());
        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (string.Equals(reader.NamespaceURI, CityGmlDocumentReferenceSystemReader.Gml.NamespaceName, StringComparison.Ordinal)
                && string.Equals(reader.LocalName, "Envelope", StringComparison.Ordinal))
            {
                coordinateReferenceSystem =
                    LocalCityGmlObjectProjection.CoordinateReferenceSystem.Parse(reader.GetAttribute("srsName"));
                parsedReferenceSystem?.Invoke(coordinateReferenceSystem);
                continue;
            }

            if (!string.Equals(reader.NamespaceURI, CityGmlAppearanceStreamingProbe.App.NamespaceName, StringComparison.Ordinal)
                && !string.Equals(reader.NamespaceURI, CityGmlAppearanceStreamingProbe.Core.NamespaceName, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(reader.NamespaceURI, CityGmlAppearanceStreamingProbe.App.NamespaceName, StringComparison.Ordinal))
            {
                switch (reader.LocalName)
                {
                    case "ParameterizedTexture":
                        using (XmlReader textureSubtreeReader = reader.ReadSubtree())
                        {
                            XElement textureElement = await XElement.LoadAsync(textureSubtreeReader, LoadOptions.None, cancellationToken);
                            appearanceStore.ApplyAppearanceElement(textureElement);
                        }

                        continue;
                    case "X3DMaterial":
                        using (XmlReader materialSubtreeReader = reader.ReadSubtree())
                        {
                            XElement materialElement = await XElement.LoadAsync(materialSubtreeReader, LoadOptions.None, cancellationToken);
                            appearanceStore.ApplyAppearanceElement(materialElement);
                        }

                        continue;
                    case "GeoreferencedTexture":
                        using (XmlReader textureSubtreeReader = reader.ReadSubtree())
                        {
                            XElement textureElement = await XElement.LoadAsync(textureSubtreeReader, LoadOptions.None, cancellationToken);
                            appearanceStore.ApplyAppearanceElement(textureElement);
                        }

                        continue;
                }
            }

            if (!string.Equals(reader.NamespaceURI, CityGmlAppearanceStreamingProbe.Core.NamespaceName, StringComparison.Ordinal)
                || !string.Equals(reader.LocalName, "cityObjectMember", StringComparison.Ordinal))
            {
                continue;
            }

            using XmlReader subtreeReader = reader.ReadSubtree();
            XElement cityObjectMember = await XElement.LoadAsync(subtreeReader, LoadOptions.None, cancellationToken);
            global::PlateauResoniteLink.Application.Importing.ParsedCityObject? cityObject =
                CityGmlSourceFileCityObjectProjection.Parse(
                    cityObjectMember,
                    sourceFile,
                    requestedMeshAreas,
                    appearanceStore,
                    lodSelector,
                    coordinateReferenceSystem,
                    lodFilteringStrategy);
            if (cityObject is not null)
            {
                yield return cityObject;
            }
        }
    }

    private static async IAsyncEnumerable<global::PlateauResoniteLink.Application.Importing.ParsedCityObject> StreamParsedCityObjectsFromDocumentCoreAsync(
        global::PlateauResoniteLink.Application.Importing.SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        LodFilteringStrategy? lodFilteringStrategy,
        ICityGmlAppearanceStoreFactory appearanceStoreFactory,
        ICityGmlLodSelector lodSelector,
        Action<LocalCityGmlObjectProjection.CoordinateReferenceSystem>? parsedReferenceSystem,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        lodFilteringStrategy ??= new LodFilteringStrategy();
        await using Stream stream = await datasetSource.OpenReadAsync(sourceFile.RelativePath, cancellationToken);
        XDocument cityModel = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        LocalCityGmlObjectProjection.CoordinateReferenceSystem coordinateReferenceSystem =
            LocalCityGmlObjectProjection.CoordinateReferenceSystem.Parse(cityModel);
        parsedReferenceSystem?.Invoke(coordinateReferenceSystem);
        ICityGmlAppearanceStore appearanceStore = appearanceStoreFactory.Create(sourceFile.RelativePath, datasetSource);
        appearanceStore.LoadFromDocument(cityModel);

        if (cityModel.Root is null)
        {
            yield break;
        }

        foreach (XElement cityObjectMember in cityModel.Root.Elements(CityGmlAppearanceStreamingProbe.Core + "cityObjectMember"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            global::PlateauResoniteLink.Application.Importing.ParsedCityObject? cityObject =
                CityGmlSourceFileCityObjectProjection.Parse(
                    cityObjectMember,
                    sourceFile,
                    requestedMeshAreas,
                    appearanceStore,
                    lodSelector,
                    coordinateReferenceSystem,
                    lodFilteringStrategy);
            if (cityObject is not null)
            {
                yield return cityObject;
            }
        }
    }

}
