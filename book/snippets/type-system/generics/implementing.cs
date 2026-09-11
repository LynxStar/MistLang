using System;

namespace Snippet;

public class GenericBox<T> : Snippet.IGenericContainer<T>
{
    private T item;

    public GenericBox(T item)
    {
        this.item = item;
    }

    public T GetItem()
    {
        return item;
    }
}
