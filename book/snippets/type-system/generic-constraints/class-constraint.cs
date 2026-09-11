using System;

namespace Snippet;

internal class RefOnly<T>
    where T : class
{
    public T Item;

    public RefOnly(T item)
    {
        Item = item;
    }

    public string Show()
    {
        return $"RefOnly: {Item}";
    }
}
