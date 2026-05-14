using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

public sealed class CommonMaterialCatalog<TItem> : IReadOnlyList<TItem>
{
    private readonly TItem[] items;

    public CommonMaterialCatalog(IEnumerable<TItem> items)
        : this(items, validate: null)
    {
    }

    internal CommonMaterialCatalog(
        IEnumerable<TItem> items,
        Action<IReadOnlyList<TItem>>? validate)
    {
        ArgumentNullException.ThrowIfNull(items);
        TItem[] copiedItems = [.. items];
        validate?.Invoke(copiedItems);
        this.items = copiedItems;
    }

    public int Count => items.Length;

    public TItem this[int index] => items[index];

    public int IndexOf(TItem item)
    {
        return Array.IndexOf(items, item);
    }

    public CommonMaterialCatalog<TOut> Select<TOut>(Func<TItem, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        TOut[] mappedItems = new TOut[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            mappedItems[i] = map(items[i]);
        }

        return new CommonMaterialCatalog<TOut>(mappedItems);
    }

    public async ValueTask<CommonMaterialCatalog<TOut>> SelectAsync<TOut>(
        Func<TItem, CancellationToken, ValueTask<TOut>> map,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(map);

        TOut[] mappedItems = new TOut[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            mappedItems[i] = await map(items[i], cancellationToken).ConfigureAwait(false);
        }

        return new CommonMaterialCatalog<TOut>(mappedItems);
    }

    public IEnumerator<TItem> GetEnumerator() => ((IEnumerable<TItem>)items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
