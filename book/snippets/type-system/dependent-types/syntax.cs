using Standard;

namespace Snippet;

public class FixedBuffer<TSize>
    where TSize : IConstInt
{
    public int GetSize()
    {
        return TSize.Value;
    }

    public string Describe()
    {
        return $"FixedBuffer<{MistInvariant.Text(TSize.Value)}>";
    }

    public FixedBuffer()
    {
    }

    public static FixedBuffer<TSize> __CreateDefault()
    {
        return new FixedBuffer<TSize>();
    }
}
