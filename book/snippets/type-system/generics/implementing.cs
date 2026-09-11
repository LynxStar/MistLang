using System;

namespace Snippet;

interface IGenericContainer<T>
{
    T GetItem();
}

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
