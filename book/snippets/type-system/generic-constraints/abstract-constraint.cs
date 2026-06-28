using System;

namespace Snippet;

public interface IOrderable
{
    int GetOrder();
}
public class Sorter<T>
    where T : IOrderable
{
    public T First;

    public T Second;

    public Sorter(T first, T second)
    {
        First = first;
        Second = second;
    }

    public int GetFirstOrder()
    {
        return First.GetOrder();
    }
}
