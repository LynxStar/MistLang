using System;

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
        return $"FixedBuffer<{TSize.Value}>";
    }

    public FixedBuffer()
    {
    }

    public static FixedBuffer<TSize> __CreateDefault()
    {
        return new FixedBuffer<TSize>();
    }
}

public class TypeIdentity
{
    public static string AcceptFive(Snippet.FixedBuffer<Const_Int_5> buf)
    {
        return $"Accepted FixedBuffer<{buf.GetSize()}>";
    }

    public TypeIdentity()
    {
    }

    public static TypeIdentity __CreateDefault()
    {
        return new TypeIdentity();
    }
}
