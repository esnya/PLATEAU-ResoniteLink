namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class LiveSendProgressSink
{
    public int AttemptedCityObjectCount;

    public int ProcessedCityObjectCount;

    public int FailedCityObjectCount;

    public int FirstQueuedCityObjectLogged;

    public int FirstPreparedCityObjectLogged;

    public int FirstImportedCityObjectLogged;

    public int FirstCityObjectPreparationStartedLogged;

    public int FirstCommonMaterialPrepLogged;

    public int FirstCityObjectStreamingStartedLogged;

    public int FirstCityObjectDequeuedLogged;

    public void Reset()
    {
        AttemptedCityObjectCount = 0;
        ProcessedCityObjectCount = 0;
        FailedCityObjectCount = 0;
        FirstQueuedCityObjectLogged = 0;
        FirstPreparedCityObjectLogged = 0;
        FirstImportedCityObjectLogged = 0;
        FirstCityObjectPreparationStartedLogged = 0;
        FirstCommonMaterialPrepLogged = 0;
        FirstCityObjectStreamingStartedLogged = 0;
        FirstCityObjectDequeuedLogged = 0;
    }
}
