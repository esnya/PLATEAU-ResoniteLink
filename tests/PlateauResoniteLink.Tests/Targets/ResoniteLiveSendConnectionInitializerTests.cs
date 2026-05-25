using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteLiveSendConnectionInitializerTests
{
    [Fact]
    public async Task EnsureConnectedAsyncConnectsWithNormalizedRequestAndReportsProgress()
    {
        DelegatingClientSession session = new();
        List<string> progressMessages = [];
        ResoniteLiveSendConnectionInitializer initializer = new();

        await initializer.EnsureConnectedAsync(
            CreateStartRequest(),
            new LiveSendRunStartContext(
                new Uri("ws://localhost:12345/"),
                session,
                ResoniteLinkSendDiagnostics.Disabled,
                progressMessages.Add),
            CancellationToken.None);

        Assert.Equal(
            [new LiveSendConnectionRequest("normalized-dataset", "normalized-mesh")],
            session.EnsureConnectedRequests);
        Assert.Contains(
            progressMessages,
            static message => message.Contains(
                "Connecting ResoniteLink connection pool to ws://localhost:12345/",
                StringComparison.Ordinal)
                && message.Contains("with 3 available routed connection(s)", StringComparison.Ordinal));
        Assert.Contains(
            progressMessages,
            static message => message.Contains("ResoniteLink connection pool ready in ", StringComparison.Ordinal)
                && message.Contains("dataset='setup-dataset'", StringComparison.Ordinal)
                && message.Contains("mesh='setup-mesh'", StringComparison.Ordinal));
    }

    private static LiveSendRunStartRequest CreateStartRequest()
    {
        return new LiveSendRunStartRequest(
            new ResoniteSceneSetupInfo(
                "setup-dataset",
                "setup-mesh",
                SourceFiles: [],
                SelectedMeshCodes: [],
                new ResoniteLicenseAttributionMetadata(
                    RequireCredit: false,
                    CreditText: null,
                    LicenseName: null,
                    LicenseUrl: null)),
            WorkRoot: "work",
            CommonMaterialCatalog.Create(),
            new PlateauImportRequest(
                Dataset: "normalized-dataset",
                MeshCode: "normalized-mesh",
                CityGmlSource: DatasetLocation.Local("dataset"),
                PackageNames: ["bldg"]),
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            ResoniteImportMemoryProfile.Large,
            ConnectionCount: 3,
            MeshBakeEnabled: true);
    }
}
