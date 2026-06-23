using System;

namespace Snippet;

class Describer<T>
{
    public T Item;

    public Describer(T item)
    {
        Item = item;
    }

    public string GetDescription()
    {
        return Item.Describe();
    }
}
