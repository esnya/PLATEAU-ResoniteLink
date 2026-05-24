using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteSharedSlotIndexFactory
{
    ResoniteSharedSlotIndex Create(
        ResoniteSceneSetupState setupState,
        ResoniteLocalOrigin requestLocalOrigin,
        IReadOnlyDictionary<string, string> sourceFileSlotNamesByRelativePath,
        Func<IResoniteLinkClient, ResoniteSlotLocator, string, ResoniteFloat3?, ResoniteFloatQ?, CancellationToken, Task<CreatedSlot>> createSlotAsync);
}

internal sealed class ResoniteSharedSlotIndexFactory : IResoniteSharedSlotIndexFactory
{
    public ResoniteSharedSlotIndex Create(
        ResoniteSceneSetupState setupState,
        ResoniteLocalOrigin requestLocalOrigin,
        IReadOnlyDictionary<string, string> sourceFileSlotNamesByRelativePath,
        Func<IResoniteLinkClient, ResoniteSlotLocator, string, ResoniteFloat3?, ResoniteFloatQ?, CancellationToken, Task<CreatedSlot>> createSlotAsync)
    {
        ArgumentNullException.ThrowIfNull(sourceFileSlotNamesByRelativePath);
        ArgumentNullException.ThrowIfNull(createSlotAsync);

        ResoniteSharedSlotIndex placement = new(
            setupState.DatasetRootSlot,
            setupState.DatasetAssetsRootSlot,
            requestLocalOrigin,
            sourceFileSlotNamesByRelativePath,
            setupState.SceneAnchor,
            createSlotAsync);
        placement.IndexSetupHierarchy(setupState);
        return placement;
    }
}
