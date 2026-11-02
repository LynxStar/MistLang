using Standard;

namespace Snippet;

internal interface IOrderable
{
    int GetOrder();
}

internal class Sorter<T>
    where T : Snippet.IOrderable
{
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public T First;
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public T Second;

    public Sorter(T first, T second)
    {
        this.First = first;
        this.Second = second;
    }

    public int GetFirstOrder()
    {
        return this.First.GetOrder();
    }
}
