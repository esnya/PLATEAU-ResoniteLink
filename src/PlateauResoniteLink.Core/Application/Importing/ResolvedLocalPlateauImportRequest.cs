using System;
using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public sealed record ResolvedLocalPlateauImportRequest
{
    public const string RemoteCityGmlResourcePrefix = "source-archive";
    public const string RemoteDemTextureResourcePrefix = "source-ortho";

    private readonly ValidatedLocalDatasetLocation? demTextureSource;

    private ResolvedLocalPlateauImportRequest(
        ValidatedPlateauImportRequest request,
        ValidatedLocalDatasetLocation cityGmlSource,
        ValidatedLocalDatasetLocation? demTextureSource)
    {
        ValidatedRequest = request with
        {
            CityGmlSource = cityGmlSource,
            DemTextureSource = demTextureSource,
        };
        CityGmlSource = cityGmlSource;
        this.demTextureSource = demTextureSource;
    }

    internal ValidatedPlateauImportRequest ValidatedRequest { get; }

    internal ValidatedLocalDatasetLocation CityGmlSource { get; }

    public string Dataset => ValidatedRequest.Dataset;

    public string MeshCode => ValidatedRequest.MeshCode;

    public string CityGmlLocalSourcePath => CityGmlSource.LocalSourcePath;

    public LocalDatasetLocation? DemTextureSource => demTextureSource is null
        ? null
        : new LocalDatasetLocation(demTextureSource.LocalSourcePath);

    public string? DemTextureLocalSourcePath => demTextureSource?.LocalSourcePath;

    public IReadOnlyList<string>? PackageNames => ValidatedRequest.PackageNames;

    public IReadOnlySet<int>? GlobalExcludeLodLevels => ValidatedRequest.GlobalExcludeLodLevels;

    public IReadOnlyDictionary<string, IReadOnlySet<int>>? ExcludeLodLevelsByPackage => ValidatedRequest.ExcludeLodLevelsByPackage;

    public IReadOnlyDictionary<string, string>? PackagePatterns => ValidatedRequest.PackagePatterns;

    public bool IncludeMarkingAlways => ValidatedRequest.IncludeMarkingAlways;

    public TerrainMeshMode TerrainMeshMode => ValidatedRequest.TerrainMeshMode;

    public double TerrainGridMetersPerVertex => ValidatedRequest.TerrainGridMetersPerVertex;

    public int TerrainGridMaxResolution => ValidatedRequest.TerrainGridMaxResolution;

    public PlateauImportRequest ToImportRequest()
    {
        return (ValidatedRequest with
        {
            CityGmlSource = CityGmlSource,
            DemTextureSource = demTextureSource,
        }).ToImportRequest();
    }

    public static ResolvedLocalPlateauImportRequest Create(
        ValidatedPlateauImportRequest request,
        ValidatedLocalDatasetLocation cityGmlSource,
        ValidatedLocalDatasetLocation? demTextureSource,
        string? workRoot = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(cityGmlSource);
        ValidateResolvedLocalSource(
            request.CityGmlSource,
            cityGmlSource,
            workRoot,
            RemoteCityGmlResourcePrefix,
            nameof(cityGmlSource));
        ValidateResolvedOptionalLocalSource(
            request.DemTextureSource,
            demTextureSource,
            workRoot,
            RemoteDemTextureResourcePrefix,
            nameof(demTextureSource));

        return new ResolvedLocalPlateauImportRequest(request, cityGmlSource, demTextureSource);
    }

    private static void ValidateResolvedLocalSource(
        ValidatedDatasetLocation requestedSource,
        ValidatedLocalDatasetLocation resolvedSource,
        string? workRoot,
        string remoteResourcePrefix,
        string parameterName)
    {
        if (requestedSource is ValidatedLocalDatasetLocation requestedLocalSource
            && !string.Equals(
                requestedLocalSource.LocalSourcePath,
                resolvedSource.LocalSourcePath,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Resolved local dataset source must match the requested local dataset source.",
                parameterName);
        }

        if (requestedSource is ValidatedRemoteDatasetLocation requestedRemoteSource)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);
            if (!RemoteDatasetResourceLayout.MatchesRemoteResourcePath(
                    workRoot,
                    requestedRemoteSource.ServerUri,
                    remoteResourcePrefix,
                    resolvedSource.LocalSourcePath))
            {
                throw new ArgumentException(
                    "Resolved local dataset source must match the requested remote dataset source cache path.",
                    parameterName);
            }
        }
    }

    private static void ValidateResolvedOptionalLocalSource(
        ValidatedDatasetLocation? requestedSource,
        ValidatedLocalDatasetLocation? resolvedSource,
        string? workRoot,
        string remoteResourcePrefix,
        string parameterName)
    {
        if (requestedSource is null)
        {
            if (resolvedSource is not null)
            {
                throw new ArgumentException(
                    "Resolved local dataset source must be omitted when no source was requested.",
                    parameterName);
            }

            return;
        }

        if (resolvedSource is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        ValidateResolvedLocalSource(requestedSource, resolvedSource, workRoot, remoteResourcePrefix, parameterName);
    }
}
