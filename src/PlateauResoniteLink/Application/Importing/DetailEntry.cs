using System;
using System.Globalization;

namespace PlateauResoniteLink.Application.Importing;

public readonly record struct DetailEntry(
    string Key,
    string DisplayName,
    int Order)
{
    public static readonly DetailEntry Default = new("detail-default", "Detail", 0);

    public static DetailEntry FromSourceRepresentationIndex(int? sourceRepresentationIndex)
    {
        if (!sourceRepresentationIndex.HasValue)
        {
            return Default;
        }

        int order = Math.Max(0, 4 - sourceRepresentationIndex.Value);
        string orderToken = order.ToString(CultureInfo.InvariantCulture);
        return new DetailEntry(
            $"detail-{orderToken}",
            $"Detail {orderToken}",
            order);
    }
}
