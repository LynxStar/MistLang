using Standard;

namespace Snippet;

public class TypedBuffer<T, TSize>
    where TSize : IConstInt
{
    public T Value;

    public TypedBuffer(T value)
    {
        this.Value = value;
    }

    public string Describe()
    {
        return $"TypedBuffer<{MistInvariant.Text(TSize.Value)}>(value={MistInvariant.Text(this.Value)})";
    }
}
