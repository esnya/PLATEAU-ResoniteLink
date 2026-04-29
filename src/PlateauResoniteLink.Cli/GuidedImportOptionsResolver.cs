using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Cli;

internal sealed class GuidedImportOptionsResolver(
    TextReader standardInput,
    TextWriter standardOutput,
    TextWriter standardError,
    DatasetInspectionService datasetInspectionService,
    IResoniteLinkTargetDiscovery targetDiscovery)
{
    public async Task<ImportCommandOptions> ResolveAsync(
        ImportCommandOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Guided)
        {
            return options;
        }

        DatasetLocation source = await ResolveSourceAsync(options.Request.Source, cancellationToken);
        GuidedDatasetInspection? inspection = await TryInspectSourceAsync(source, cancellationToken);

        IReadOnlyList<string> packageNames = await ResolvePackageNamesAsync(
            options.Request.PackageNames ?? CliDefaultOptions.PackageNames,
            options.PackageNamesSpecified,
            inspection,
            cancellationToken);

        IReadOnlySet<int>? globalExcludeLods = await ResolveGlobalExcludeLodsAsync(
            options.Request.GlobalExcludeLodLevels,
            options.GlobalExcludeLodLevelsSpecified,
            inspection,
            cancellationToken);

        string dataset = await ResolveDatasetAsync(options.Request.Dataset, cancellationToken);
        string meshCode = await ResolveMeshCodeAsync(options.Request.MeshCode, inspection, cancellationToken);
        await TryWriteSearchPreviewAsync(source, meshCode, packageNames, cancellationToken);

        Uri resoniteLinkUri = await ResolveResoniteLinkUriAsync(options.ResoniteLinkUri, cancellationToken);

        return options with
        {
            Request = options.Request with
            {
                Dataset = dataset,
                MeshCode = meshCode,
                Source = source,
                PackageNames = packageNames,
                GlobalExcludeLodLevels = globalExcludeLods,
            },
            ResoniteLinkUri = resoniteLinkUri,
        };
    }

    private async Task<DatasetLocation> ResolveSourceAsync(
        DatasetLocation source,
        CancellationToken cancellationToken)
    {
        if (!IsMissingDatasetLocation(source))
        {
            return source;
        }

        string sourceInput = await PromptRequiredValueAsync(
            "CityGML source path or URL (--citygml-source)",
            static value =>
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return "Specify --citygml-source.";
                }

                return CliArgumentsParser.TryParseDatasetLocationInput(value, out _, out string? error)
                    ? null
                    : error;
            },
            cancellationToken);
        _ = CliArgumentsParser.TryParseDatasetLocationInput(sourceInput, out DatasetLocation? parsedSource, out _);
        return parsedSource!;
    }

    private async Task<GuidedDatasetInspection?> TryInspectSourceAsync(
        DatasetLocation source,
        CancellationToken cancellationToken)
    {
        if (source is not LocalDatasetLocation localSource
            || string.IsNullOrWhiteSpace(localSource.LocalSourcePath))
        {
            return null;
        }

        try
        {
            DatasetStatsResult stats = await datasetInspectionService.GetStatsAsync(
                localSource.LocalSourcePath,
                packageNames: null,
                cancellationToken);

            string[] packageNames = stats.PackageCounts
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => pair.Key)
                .ToArray();
            string[] meshCodes = stats.MeshCodeCounts
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => pair.Key)
                .ToArray();
            int[] lodLevels = stats.LodCoverageCounts
                .OrderBy(static pair => pair.Key)
                .Select(static pair => pair.Key)
                .ToArray();

            await standardOutput.WriteLineAsync("Dataset inspection:");
            await standardOutput.WriteLineAsync($"  Recognized source files: {stats.RecognizedSourceFileCount.ToString(CultureInfo.InvariantCulture)}");
            await standardOutput.WriteLineAsync($"  Packages: {FormatCounts(stats.PackageCounts)}");
            await standardOutput.WriteLineAsync($"  Mesh codes: {FormatCounts(stats.MeshCodeCounts)}");
            await standardOutput.WriteLineAsync($"  LOD coverage: {FormatCounts(stats.LodCoverageCounts)}");

            return new GuidedDatasetInspection(packageNames, meshCodes, lodLevels);
        }
        catch (PlateauImportValidationException exception)
        {
            await standardError.WriteLineAsync(
                $"Dataset inspection unavailable: {string.Join(" ", exception.Errors)}");
            return null;
        }
    }

    private async Task<IReadOnlyList<string>> ResolvePackageNamesAsync(
        IReadOnlyList<string> currentPackageNames,
        bool packageNamesSpecified,
        GuidedDatasetInspection? inspection,
        CancellationToken cancellationToken)
    {
        if (packageNamesSpecified || inspection is null || inspection.PackageNames.Count == 0)
        {
            return currentPackageNames;
        }

        await WriteNumberedValuesAsync("Available packages", inspection.PackageNames, cancellationToken);
        string detectedPackageDefault = string.Join(",", inspection.PackageNames);
        string? packageInput = await PromptOptionalValueAsync(
            $"Packages (--packages, blank keeps {detectedPackageDefault})",
            defaultValue: detectedPackageDefault,
            value =>
            {
                string normalizedValue = ResolveCsvSelectionsOrValue(value, inspection.PackageNames);
                _ = CliArgumentsParser.ParsePackageNames(normalizedValue, out string? packageError);
                return packageError;
            },
            cancellationToken);

        string normalizedInput = ResolveCsvSelectionsOrValue(packageInput ?? detectedPackageDefault, inspection.PackageNames);
        return CliArgumentsParser.ParsePackageNames(normalizedInput, out _);
    }

    private async Task<IReadOnlySet<int>?> ResolveGlobalExcludeLodsAsync(
        IReadOnlySet<int>? currentGlobalExcludeLods,
        bool globalExcludeLodsSpecified,
        GuidedDatasetInspection? inspection,
        CancellationToken cancellationToken)
    {
        if (globalExcludeLodsSpecified || inspection is null || inspection.LodLevels.Count == 0)
        {
            return currentGlobalExcludeLods;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await standardOutput.WriteLineAsync(
            $"Detected LOD levels: {string.Join(", ", inspection.LodLevels.Select(static lod => lod.ToString(CultureInfo.InvariantCulture)))}");
        string? excludeInput = await PromptOptionalValueAsync(
            "Exclude global LOD levels (--exclude-lod, blank keeps none)",
            defaultValue: null,
            value => CliArgumentsParser.TryParseLodExclusionRules(value, out _, out string? lodError)
                ? null
                : lodError,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(excludeInput))
        {
            return currentGlobalExcludeLods;
        }

        _ = CliArgumentsParser.TryParseLodExclusionRules(excludeInput, out IReadOnlySet<int>? parsedLods, out _);
        return parsedLods;
    }

    private async Task<string> ResolveDatasetAsync(string dataset, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(dataset))
        {
            return dataset;
        }

        return await PromptRequiredValueAsync(
            "PLATEAU dataset (--dataset)",
            static value => string.IsNullOrWhiteSpace(value) ? "Specify --dataset." : null,
            cancellationToken);
    }

    private async Task<string> ResolveMeshCodeAsync(
        string meshCode,
        GuidedDatasetInspection? inspection,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(meshCode))
        {
            return meshCode;
        }

        if (inspection is not null && inspection.MeshCodes.Count > 0)
        {
            await WriteNumberedValuesAsync("Available mesh codes", inspection.MeshCodes, cancellationToken);
            string? defaultSelection = inspection.MeshCodes.Count == 1 ? "1" : null;
            string prompt = defaultSelection is null
                ? "Mesh code or regex (--mesh-code, number from list accepted)"
                : $"Mesh code or regex (--mesh-code, blank keeps {inspection.MeshCodes[0]})";
            string? selectedInput = await PromptOptionalValueAsync(
                prompt,
                defaultSelection,
                value => string.IsNullOrWhiteSpace(value) ? "Specify --mesh-code." : null,
                cancellationToken,
                allowBlank: defaultSelection is not null);

            return ResolveSelectionOrValue(selectedInput!, inspection.MeshCodes);
        }

        return await PromptRequiredValueAsync(
            "Mesh code or regex (--mesh-code)",
            static value => string.IsNullOrWhiteSpace(value) ? "Specify --mesh-code." : null,
            cancellationToken);
    }

    private async Task TryWriteSearchPreviewAsync(
        DatasetLocation source,
        string meshCode,
        IReadOnlyList<string> packageNames,
        CancellationToken cancellationToken)
    {
        if (source is not LocalDatasetLocation localSource
            || string.IsNullOrWhiteSpace(localSource.LocalSourcePath))
        {
            return;
        }

        DatasetSearchResult result = await datasetInspectionService.SearchAsync(
            localSource.LocalSourcePath,
            meshCode,
            packageNames,
            cancellationToken);

        await standardOutput.WriteLineAsync("Search preview:");
        await standardOutput.WriteLineAsync($"  Selected mesh codes: {FormatCsv(result.SelectedMeshCodes)}");
        await standardOutput.WriteLineAsync($"  Matched source files: {result.SourceFiles.Count.ToString(CultureInfo.InvariantCulture)}");
    }

    private async Task<Uri> ResolveResoniteLinkUriAsync(
        Uri? resoniteLinkUri,
        CancellationToken cancellationToken)
    {
        if (resoniteLinkUri is not null)
        {
            return resoniteLinkUri;
        }

        IReadOnlyList<ResoniteLinkTarget> targets = await targetDiscovery.DiscoverAsync(cancellationToken);
        if (targets.Count > 0)
        {
            await WriteDiscoveredTargetsAsync(targets, cancellationToken);
        }

        string? defaultSelection = targets.Count == 1 ? "1" : null;
        string prompt = targets.Count > 0
            ? "ResoniteLink endpoint (--resonitelink-port or --resonitelink-url, number from discovered list accepted)"
            : "ResoniteLink endpoint (--resonitelink-port or --resonitelink-url)";
        string endpointInput = await PromptOptionalValueAsync(
            prompt,
            defaultSelection,
            value => ValidateEndpointInput(value, targets),
            cancellationToken,
            allowBlank: false) ?? string.Empty;

        string normalizedInput = ResolveTargetSelectionOrValue(endpointInput, targets);
        _ = CliArgumentsParser.TryParseResoniteLinkEndpointInput(normalizedInput, out Uri? parsedUri, out _);
        return parsedUri!;
    }

    private static string? ValidateEndpointInput(string value, IReadOnlyList<ResoniteLinkTarget> targets)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Specify either --resonitelink-port or --resonitelink-url.";
        }

        string normalizedInput = ResolveTargetSelectionOrValue(value, targets);
        return CliArgumentsParser.TryParseResoniteLinkEndpointInput(normalizedInput, out _, out string? error)
            ? null
            : error;
    }

    private async Task<string> PromptRequiredValueAsync(
        string promptLabel,
        Func<string, string?> validate,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            string value = await ReadPromptValueAsync(promptLabel, cancellationToken);
            string? error = validate(value);
            if (error is null)
            {
                return value;
            }

            await standardError.WriteLineAsync(error);
        }
    }

    private async Task<string?> PromptOptionalValueAsync(
        string promptLabel,
        string? defaultValue,
        Func<string, string?> validate,
        CancellationToken cancellationToken,
        bool allowBlank = true)
    {
        while (true)
        {
            string value = await ReadPromptValueAsync(promptLabel, cancellationToken);
            if (string.IsNullOrWhiteSpace(value) && defaultValue is not null)
            {
                value = defaultValue;
            }
            else if (string.IsNullOrWhiteSpace(value) && allowBlank)
            {
                return null;
            }

            string? error = validate(value);
            if (error is null)
            {
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            await standardError.WriteLineAsync(error);
        }
    }

    private async Task<string> ReadPromptValueAsync(
        string promptLabel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await standardOutput.WriteAsync($"{promptLabel}: ");
        await standardOutput.FlushAsync(cancellationToken);

        string? input = await standardInput.ReadLineAsync(cancellationToken);
        if (input is null)
        {
            throw new PlateauImportValidationException([$"No input received for {promptLabel}."]);
        }

        return input.Trim();
    }

    private async Task WriteNumberedValuesAsync(
        string label,
        IReadOnlyList<string> values,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await standardOutput.WriteLineAsync($"{label}:");
        for (int index = 0; index < values.Count; index++)
        {
            await standardOutput.WriteLineAsync($"  {index + 1}. {values[index]}");
        }
    }

    private async Task WriteDiscoveredTargetsAsync(
        IReadOnlyList<ResoniteLinkTarget> targets,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await standardOutput.WriteLineAsync("Discovered ResoniteLink targets:");
        for (int index = 0; index < targets.Count; index++)
        {
            ResoniteLinkTarget target = targets[index];
            string name = string.IsNullOrWhiteSpace(target.SessionName)
                ? "(unnamed session)"
                : target.SessionName;
            await standardOutput.WriteLineAsync($"  {index + 1}. {name} | {target.Endpoint}");
        }
    }

    private static string ResolveSelectionOrValue(string input, IReadOnlyList<string> values)
    {
        if (int.TryParse(input, out int selection) && selection >= 1 && selection <= values.Count)
        {
            return values[selection - 1];
        }

        return input;
    }

    private static string ResolveCsvSelectionsOrValue(string input, IReadOnlyList<string> values)
    {
        string[] parts = input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return input;
        }

        List<string> selectedValues = [];
        foreach (string part in parts)
        {
            if (!int.TryParse(part, out int selection) || selection < 1 || selection > values.Count)
            {
                return input;
            }

            selectedValues.Add(values[selection - 1]);
        }

        return string.Join(",", selectedValues);
    }

    private static string ResolveTargetSelectionOrValue(string input, IReadOnlyList<ResoniteLinkTarget> targets)
    {
        if (int.TryParse(input, out int selection) && selection >= 1 && selection <= targets.Count)
        {
            return targets[selection - 1].Endpoint.AbsoluteUri;
        }

        return input;
    }

    private static bool IsMissingDatasetLocation(DatasetLocation source)
    {
        return source switch
        {
            LocalDatasetLocation localSource => string.IsNullOrWhiteSpace(localSource.LocalSourcePath),
            RemoteDatasetLocation remoteSource => remoteSource.ServerUri is null,
            _ => true,
        };
    }

    private static string FormatCsv(IReadOnlyList<string> values)
    {
        return values.Count == 0 ? "(none)" : string.Join(", ", values);
    }

    private static string FormatCounts<TKey>(IReadOnlyDictionary<TKey, int> counts)
        where TKey : notnull
    {
        return counts.Count == 0
            ? "(none)"
            : string.Join(
                ", ",
                counts.Select(
                    static pair => $"{pair.Key}={pair.Value.ToString(CultureInfo.InvariantCulture)}"));
    }

    private sealed record GuidedDatasetInspection(
        IReadOnlyList<string> PackageNames,
        IReadOnlyList<string> MeshCodes,
        IReadOnlyList<int> LodLevels);
}
