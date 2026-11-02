using Standard;

namespace Snippet;

internal class ValOnly<T>
    where T : struct
{
    public T Item;

    public ValOnly(T item)
    {
        this.Item = item;
    }

    public string Show()
    {
        return $"ValOnly: {MistInvariant.Text(this.Item)}";
    }
}
