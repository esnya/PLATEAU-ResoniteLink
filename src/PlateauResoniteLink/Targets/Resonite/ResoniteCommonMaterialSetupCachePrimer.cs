using System;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteCommonMaterialSetupCachePrimer
{
    void Prime(
        ResoniteSceneSetupState setupState,
        CommonMaterialAssetCache materials,
        LiveSendProgressSink progress,
        Action<string>? progressReporter);
}

internal sealed class ResoniteCommonMaterialSetupCachePrimer : IResoniteCommonMaterialSetupCachePrimer
{
    public void Prime(
        ResoniteSceneSetupState setupState,
        CommonMaterialAssetCache materials,
        LiveSendProgressSink progress,
        Action<string>? progressReporter)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(progress);

        foreach (CommonMaterialCatalogMember<ResoniteCommonMaterialAsset> materialAsset in setupState.CommonMaterialAssets.EnumerateMembers())
        {
            materials.CommonMaterialAssets.Set(materialAsset.Item);
        }

        foreach (string family in setupState.CommonMaterialFamilies)
        {
            materials.CommonMaterialFamilyWarmupTasks[family] = Task.CompletedTask;
        }

        if (setupState.CommonMaterialAssets.Count > 0)
        {
            progress.FirstCommonMaterialPrepLogged = setupState.CommonMaterialAssets.Count;
            ReportProgress(
                progressReporter,
                PlateauLog.Info(
                    "live",
                    $"Setup batch prepared {setupState.CommonMaterialAssets.Count} textureless common materials."));
        }
        else
        {
            ReportProgress(
                progressReporter,
                PlateauLog.Info(
                    "live",
                    "Setup created common material slots; no textureless common material components were needed in setup batch."));
        }
    }

    private static void ReportProgress(Action<string>? progressReporter, string message)
    {
        progressReporter?.Invoke(message);
    }
}
