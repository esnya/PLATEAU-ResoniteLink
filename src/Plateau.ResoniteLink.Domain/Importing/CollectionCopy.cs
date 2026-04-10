using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace Plateau.ResoniteLink.Domain.Importing;

internal static class CollectionCopy
{
    public static IReadOnlyList<T> List<T>(IReadOnlyList<T> values, string paramName)
    {
        ArgumentNullException.ThrowIfNull(values, paramName);

        for (int index = 0; index < values.Count; index++)
        {
            if (values[index] is null)
            {
                throw new ArgumentException("Collection cannot contain null elements.", paramName);
            }
        }

        return values.Count == 0 ? Array.Empty<T>() : new ReadOnlyCollection<T>([.. values]);
    }

    public static IReadOnlyList<T>? ListOrNull<T>(IReadOnlyList<T>? values)
    {
        return values is null ? null : List(values, nameof(values));
    }

    public static IReadOnlySet<T>? SetOrNull<T>(IReadOnlySet<T>? values)
        where T : notnull
    {
        return values?.ToFrozenSet();
    }

    public static IReadOnlyDictionary<TKey, TValue>? DictionaryOrNull<TKey, TValue>(IReadOnlyDictionary<TKey, TValue>? values)
        where TKey : notnull
    {
        return values?.ToFrozenDictionary();
    }

    public static IReadOnlyDictionary<string, IReadOnlySet<int>>? NestedSetDictionaryOrNull(
        IReadOnlyDictionary<string, IReadOnlySet<int>>? values)
    {
        return values?.ToFrozenDictionary(
            static entry => entry.Key,
            static entry => (IReadOnlySet<int>)entry.Value.ToFrozenSet(),
            StringComparer.Ordinal);
    }
}
