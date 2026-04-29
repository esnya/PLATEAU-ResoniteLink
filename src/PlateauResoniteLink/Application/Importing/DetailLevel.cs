using System;

namespace PlateauResoniteLink.Application.Importing;

public readonly record struct DetailLevel(int Order) : IComparable<DetailLevel>
{
    public int CompareTo(DetailLevel other)
    {
        return Order.CompareTo(other.Order);
    }

    public static bool operator <(DetailLevel left, DetailLevel right) => left.CompareTo(right) < 0;

    public static bool operator <=(DetailLevel left, DetailLevel right) => left.CompareTo(right) <= 0;

    public static bool operator >(DetailLevel left, DetailLevel right) => left.CompareTo(right) > 0;

    public static bool operator >=(DetailLevel left, DetailLevel right) => left.CompareTo(right) >= 0;
}
