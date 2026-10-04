using Standard;

namespace Snippet;

public class TypedBuffer<T, TSize>
    where TSize : IConstIntegral
{
    public T Value;
    public static int Size => ((int)TSize.Value);

    public TypedBuffer(T value)
    {
        this.Value = value;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"TypedBuffer<{((int)TSize.Value)}>(value={MistInvariant.Text(this.Value)})");
    }
}
