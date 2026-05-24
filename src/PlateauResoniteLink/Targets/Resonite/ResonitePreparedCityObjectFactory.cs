using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResonitePreparedCityObjectFactory
{
    Task<PreparedCityObject> CreateAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        ResoniteConstructionCityObject cityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);
}

internal sealed class ResonitePreparedCityObjectFactory(
    IResonitePreparedGeometryFactory preparedGeometryFactory,
    IResonitePreparedTextureReferenceFactory textureReferenceFactory) : IResonitePreparedCityObjectFactory
{
    private readonly IResonitePreparedGeometryFactory preparedGeometryFactory =
        preparedGeometryFactory ?? throw new ArgumentNullException(nameof(preparedGeometryFactory));
    private readonly IResonitePreparedTextureReferenceFactory textureReferenceFactory =
        textureReferenceFactory ?? throw new ArgumentNullException(nameof(textureReferenceFactory));

    public async Task<PreparedCityObject> CreateAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        ResoniteConstructionCityObject cityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(routedClient);
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(diagnostics);

        preparedGeometryFactory.ValidateForPreparation(cityObject);
        Task<PreparedConstructionGeometry> geometryPreparationTask =
            preparedGeometryFactory.CreateAsync(cityObject, cancellationToken);
        Task<PreparedTextureReference[]> texturesPreparationTask = textureReferenceFactory.CreateAsync(
            state,
            routedClient,
            cityObject,
            progressReporter,
            cancellationToken);
        Stopwatch stopwatch = Stopwatch.StartNew();
        PreparedTextureReference[] preparedTextures = await texturesPreparationTask;
        PreparedConstructionGeometry preparedGeometry = await geometryPreparationTask;
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay = preparedTextures
            .Where(static texture => texture is { TerrainOverlay: not null, GeneratedTerrainTexture: not null })
            .ToDictionary(
                static texture => texture.TerrainOverlay!,
                static texture => texture.GeneratedTerrainTexture!);
        cityObject = preparedGeometryFactory.ApplyTerrainTextureCanvasUv(cityObject, preparedTerrainTextureDataByOverlay);
        preparedGeometry = preparedGeometryFactory.RecreateStaticMeshIfNeeded(cityObject, preparedGeometry);
        stopwatch.Stop();
        diagnostics.RecordPrepare(cityObject.PackageName, stopwatch.Elapsed.TotalSeconds);

        if (Interlocked.CompareExchange(ref state.Progress.FirstPreparedCityObjectLogged, 1, 0) == 0)
        {
            progressReporter?.Invoke(
                PlateauLog.Info(
                    "live",
                    $"First city object prepared in {stopwatch.Elapsed.TotalSeconds:F3}s "
                    + $"after scene start {state.Runtime.ElapsedTotalSeconds:F3}s: "
                    + $"{cityObject.DisplayName} "
                    + $"(textures={preparedTextures.Length}, geometry={PreparedConstructionGeometryFormatter.Describe(preparedGeometry)})."));
        }

        return new PreparedCityObject(
            cityObject,
            preparedGeometry,
            preparedTextures);
    }

}
