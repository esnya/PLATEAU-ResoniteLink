using System;
using System.Globalization;

namespace PlateauResoniteLink.Targets.Resonite;

public readonly record struct RenderStage(
    string Key,
    string DisplayName,
    int Order)
{
    public static readonly RenderStage Default = new("detail-default", "Detail", 0);

    public static RenderStage FromSourceRepresentationIndex(int? sourceRepresentationIndex)
    {
        if (!sourceRepresentationIndex.HasValue)
        {
            return Default;
        }

        int order = Math.Max(0, 4 - sourceRepresentationIndex.Value);
        string orderToken = order.ToString(CultureInfo.InvariantCulture);
        return new RenderStage(
            $"detail-{orderToken}",
            $"Detail {orderToken}",
            order);
    }
}
