using Standard;

namespace Snippet;

public class GenericBox<T> : Snippet.IGenericContainer<T>
{
    [System.Diagnostics.CodeAnalysis.AllowNull]
    private T item;

    public GenericBox(T item)
    {
        this.item = item;
    }

    public T GetItem()
    {
        return this.item;
    }
}
