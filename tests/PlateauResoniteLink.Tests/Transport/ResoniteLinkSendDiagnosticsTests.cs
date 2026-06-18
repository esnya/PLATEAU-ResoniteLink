
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

using PlateauResoniteLink.Core.Diagnostics;
using PlateauResoniteLink.Resonite.Transport.ResoniteLink;

namespace PlateauResoniteLink.Tests.Transport;

public sealed class ResoniteLinkSendDiagnosticsTests
{
    private static readonly HashSet<string> ExpectedInstrumentNames =
    [
        "plateauresonitelink.resonite_import.city_objects",
        "plateauresonitelink.resonite_import.city_object.duration",
        "plateauresonitelink.resonite_import.prepare.duration",
        "plateauresonitelink.resonite_import.run.duration",
        "plateauresonitelink.resonitelink.rpc_calls",
        "plateauresonitelink.resonitelink.rpc_per_city_object",
    ];

    [Fact]
    public void CompleteSendWindowReportsOnlyTrackedOutcomes()
    {
        using RecordingPlateauEventListener eventListener = new();
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.CreateEnabled();

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

        string summary = Assert.Single(
            eventListener.Messages,
            static message => message.Contains("send_window_s=", StringComparison.Ordinal)
                && message.Contains("skipped_mesh_import_failure=1", StringComparison.Ordinal));
        Assert.Contains("sent=1", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("skipped_duplicate=", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("skipped_existing=", summary, StringComparison.Ordinal);
        Assert.Contains("skipped_mesh_import_failure=1", summary, StringComparison.Ordinal);
        Assert.Contains("avg_rpc_per_sent=1.00", summary, StringComparison.Ordinal);
        Assert.Contains("total_rpc=2", summary, StringComparison.Ordinal);
        Assert.Contains(
            eventListener.Messages,
            static message => message.Contains("rpc_breakdown add_slot=1, import_mesh=1", StringComparison.Ordinal));
    }

    [Fact]
    public void EnabledDiagnosticsPublishMeasurementsOnCanonicalMeterWithResoniteImportNames()
    {
        HashSet<string> measuredInstrumentNames = [];
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (string.Equals(instrument.Meter.Name, PlateauDiagnostics.MeterName, StringComparison.Ordinal)
                && ExpectedInstrumentNames.Contains(instrument.Name))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => measuredInstrumentNames.Add(instrument.Name));
        listener.SetMeasurementEventCallback<double>((instrument, _, _, _) => measuredInstrumentNames.Add(instrument.Name));
        listener.Start();

        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.CreateEnabled();
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
        listener.RecordObservableInstruments();

        Assert.Subset(measuredInstrumentNames, ExpectedInstrumentNames);
    }
}
