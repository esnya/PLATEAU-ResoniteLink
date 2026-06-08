using System;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Tests.Transport;

public sealed class ResoniteLinkSendDiagnosticsTests
{
    [Fact]
    public void CompleteSendWindowReportsOnlyTrackedOutcomes()
    {
        RecordingLogger logger = new();
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.CreateEnabled(logger);

        diagnostics.StartSendWindow(connectionCount: 2);
        diagnostics.RecordPrepare("bldg", 0.125);

        using (ResoniteLinkSendDiagnostics.CityObjectSendScope sentScope = diagnostics.BeginCityObjectSend("bldg"))
        {
            diagnostics.RecordRpcCall("add_slot");
            sentScope.MarkSent();
        }

        using (ResoniteLinkSendDiagnostics.CityObjectSendScope skippedScope = diagnostics.BeginCityObjectSend("dem"))
        {
            diagnostics.RecordRpcCall("import_mesh");
            skippedScope.MarkSkippedMeshImportFailure();
        }

        diagnostics.CompleteSendWindow();

        string summary = Assert.Single(logger.Messages, static message => message.Contains("send_window_s=", StringComparison.Ordinal));
        Assert.Contains("sent=1", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("skipped_duplicate=", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("skipped_existing=", summary, StringComparison.Ordinal);
        Assert.Contains("skipped_mesh_import_failure=1", summary, StringComparison.Ordinal);
        Assert.Contains("avg_rpc_per_sent=1.00", summary, StringComparison.Ordinal);
        Assert.Contains("total_rpc=2", summary, StringComparison.Ordinal);
        Assert.Contains(
            logger.Messages,
            static message => message.Contains("rpc_breakdown add_slot=1, import_mesh=1", StringComparison.Ordinal));
    }
}
