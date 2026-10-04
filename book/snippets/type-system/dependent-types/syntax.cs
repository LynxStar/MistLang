using Standard;

namespace Snippet;

public class FixedBuffer<TSize>
    where TSize : IConstIntegral
{
    public static int Size => ((int)TSize.Value);

    public int GetSize()
    {
        return ((int)TSize.Value);
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"FixedBuffer<{((int)TSize.Value)}>");
    }

    public FixedBuffer()
    {
    }

    public static FixedBuffer<TSize> __CreateDefault()
    {
        return new FixedBuffer<TSize>();
    }
}
