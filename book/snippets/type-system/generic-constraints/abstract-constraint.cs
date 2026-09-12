using Standard;

namespace Snippet;

internal interface IOrderable
{
    int GetOrder();
}

internal class Sorter<T>
    where T : Snippet.IOrderable
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
