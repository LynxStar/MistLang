using Standard;

namespace Snippet;

internal class RefOnly<T>
    where T : class
{
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public T Item;

    public RefOnly(T item)
    {
        this.Item = item;
    }

    public string Show()
    {
        return $"RefOnly: {MistInvariant.Text(this.Item)}";
    }
}
