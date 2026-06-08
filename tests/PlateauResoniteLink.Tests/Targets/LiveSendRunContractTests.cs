using System;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class LiveSendRunContractTests
{
    [Fact]
    public void StartRequest_RejectsIncompleteRunInputsAtConstruction()
    {
        ResoniteSceneSetupInfo setupInfo = CreateSetupInfo();

        Assert.Throws<ArgumentNullException>(() => new LiveSendRunStartRequest(
            null!,
            "work",
            CommonMaterialCatalog.Create(),
            new LiveSendConnectionRequest("dataset", "53394525"),
            new ResoniteLocalOrigin(0, 0, 0),
            ResoniteImportMemoryProfile.Large,
            1));
        Assert.Throws<ArgumentException>(() => new LiveSendRunStartRequest(
            setupInfo,
            " ",
            CommonMaterialCatalog.Create(),
            new LiveSendConnectionRequest("dataset", "53394525"),
            new ResoniteLocalOrigin(0, 0, 0),
            ResoniteImportMemoryProfile.Large,
            1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSendRunStartRequest(
            setupInfo,
            "work",
            CommonMaterialCatalog.Create(),
            new LiveSendConnectionRequest("dataset", "53394525"),
            new ResoniteLocalOrigin(0, 0, 0),
            ResoniteImportMemoryProfile.Large,
            0));
    }

    [Fact]
    public void RunContexts_RejectMissingSessionStateAtConstruction()
    {
        Assert.Throws<ArgumentNullException>(() => new LiveSendRunStartContext(
            null!,
            new DelegatingClientSession(),
            ResoniteLinkSendDiagnostics.Disabled,
            ProgressReporter: null));
        Assert.Throws<ArgumentNullException>(() => new LiveSendRunExecutionContext(
            new Uri("ws://localhost:12345/"),
            1,
            null!,
            ResoniteLinkSendDiagnostics.Disabled,
            ProgressReporter: null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSendRunExecutionContext(
            new Uri("ws://localhost:12345/"),
            0,
            new DelegatingClientSession(),
            ResoniteLinkSendDiagnostics.Disabled,
            ProgressReporter: null));
    }

    [Fact]
    public void QueueAndWorkerContracts_RejectNonExecutableInputsAtConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSendQueuePlan(
            ConnectionCount: 0,
            QueueCapacity: 1,
            MemoryBudgetBytes: 1));
        Assert.Throws<ArgumentNullException>(() => new LiveSendEnqueueContext(
            ConnectionCount: 1,
            GetRoutedClient: null!,
            ProgressReporter: null));
        Assert.Throws<ArgumentNullException>(() => new LiveSendWorkerContext(
            new Uri("ws://localhost:12345/"),
            ConnectionCount: 1,
            GetRoutedClient: null!,
            ResoniteLinkSendDiagnostics.Disabled,
            ProgressReporter: null));
    }

    private static ResoniteSceneSetupInfo CreateSetupInfo()
    {
        return new ResoniteSceneSetupInfo(
            "dataset",
            "53394525",
            Array.Empty<string>(),
            Array.Empty<string>(),
            new ResoniteLicenseAttributionMetadata(
                RequireCredit: false,
                CreditText: null,
                LicenseName: null,
                LicenseUrl: null));
    }
}
