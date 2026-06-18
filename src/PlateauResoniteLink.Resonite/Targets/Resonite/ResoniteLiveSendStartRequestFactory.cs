using System;
using System.Collections.Generic;

using PlateauResoniteLink.Resonite.Transport.ResoniteLink;
using PlateauResoniteLink.Core.Application.Importing;
using PlateauResoniteLink.Core.Application.Importing.Contracts;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal static class ResoniteLiveSendStartRequestFactory
{
    public static LiveSendRunStartRequest Create(
        SceneImportExecutionPlan plan,
        ResoniteImportMemoryProfile memoryProfile,
        int connectionCount,
        bool meshBakeEnabled,
        bool distanceCullingEnabled)
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
            meshBakeEnabled,
            distanceCullingEnabled);
    }

    private static ResoniteSceneSetupInfo CreateSceneSetupInfo(SceneImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ResoniteSceneSetupInfo(
            request.Metadata.Request.Dataset,
            request.Metadata.Request.MeshCode,
            request.Metadata.SourceDataset.SourceFiles,
            request.Metadata.SourceDataset.SelectedMeshCodes ?? [],
            request.Metadata.SourceDataset.SourceFilePackageNamesByRelativePath ?? new Dictionary<string, string>(StringComparer.Ordinal),
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
