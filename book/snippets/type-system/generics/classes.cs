using Standard;

namespace Snippet;

public class GenericBox<T>
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

    public string Describe()
    {
        return $"GenericBox({MistInvariant.Text(this.item)})";
    }
}
