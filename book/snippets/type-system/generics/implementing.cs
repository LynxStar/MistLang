using Standard;
using System;

namespace GenericTypes;

public class GenericBox<T> : IGenericContainer<T>
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
