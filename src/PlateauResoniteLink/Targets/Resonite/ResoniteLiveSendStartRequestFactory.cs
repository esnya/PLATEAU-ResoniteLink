using System;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendStartRequestFactory
{
    LiveSendRunStartRequest Create(
        SceneImportExecutionPlan plan,
        ResoniteImportMemoryProfile memoryProfile,
        int connectionCount,
        bool meshBakeEnabled);
}

internal sealed class ResoniteLiveSendStartRequestFactory : IResoniteLiveSendStartRequestFactory
{
    public LiveSendRunStartRequest Create(
        SceneImportExecutionPlan plan,
        ResoniteImportMemoryProfile memoryProfile,
        int connectionCount,
        bool meshBakeEnabled)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentOutOfRangeException.ThrowIfLessThan(connectionCount, 1);

        SceneImportRequest request = plan.SceneImportRequest;
        return new LiveSendRunStartRequest(
            CreateSceneSetupInfo(request),
            request.WorkRoot,
            request.CommonMaterials,
            plan.NormalizedRequest,
            CreateLocalOrigin(request.Metadata.GeodeticOrigin),
            memoryProfile,
            connectionCount,
            meshBakeEnabled);
    }

    private static ResoniteSceneSetupInfo CreateSceneSetupInfo(SceneImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ResoniteSceneSetupInfo(
            request.Metadata.Request.Dataset,
            request.Metadata.Request.MeshCode,
            request.Metadata.SourceDataset.SourceFiles,
            request.Metadata.SourceDataset.SelectedMeshCodes ?? [],
            new ResoniteLicenseAttributionMetadata(
                request.Metadata.Attribution.DatasetLicense.RequireCredit,
                request.Metadata.Attribution.DatasetLicense.CreditText,
                request.Metadata.Attribution.DatasetLicense.LicenseName,
                request.Metadata.Attribution.DatasetLicense.LicenseUrl));
    }

    private static ResoniteLocalOrigin CreateLocalOrigin(GeodeticOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return new ResoniteLocalOrigin(origin.Latitude, origin.Longitude, origin.Altitude);
    }
}
