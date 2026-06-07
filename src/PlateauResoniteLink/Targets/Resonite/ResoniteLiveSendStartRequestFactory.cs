using System;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class ResoniteLiveSendStartRequestFactory
{
    public static LiveSendRunStartRequest Create(
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
            new LiveSendConnectionRequest(request.Metadata.Request.Dataset, request.Metadata.Request.MeshCode),
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
