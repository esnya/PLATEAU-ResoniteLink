using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendExecutionResultFactory
{
    SceneImportExecutionResult Create(
        IReadOnlyList<string> destinations,
        LiveSendRunState state);
}

internal sealed class ResoniteLiveSendExecutionResultFactory : IResoniteLiveSendExecutionResultFactory
{
    public SceneImportExecutionResult Create(
        IReadOnlyList<string> destinations,
        LiveSendRunState state)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(state);

        return new SceneImportExecutionResult(
            destinations,
            state.Progress.ProcessedCityObjectCount,
            state.Progress.FailedCityObjectCount,
            CreateDataSourceUsages(state));
    }

    private static ImportDataSourceUsage[] CreateDataSourceUsages(LiveSendRunState state)
    {
        return state.DemSourceUseCounts
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new ImportDataSourceUsage(
                ImportDataSourceCategory.DemTextureSource,
                pair.Key,
                pair.Value))
            .ToArray();
    }
}
