using System;

namespace Snippet;

internal class MultiFactory<T>
    where T : Mist.Emission.CSharp.__IConstruct_int<T>, Mist.Emission.CSharp.__IConstruct_bool<T>, new()
{
    public T CreateDefault()
    {
        return new T();
    }

    public T CreateFromInt(int value)
    {
        return T.__New(value);
    }

    public T CreateFromBool(bool flag)
    {
        return T.__New(flag);
    }

    public MultiFactory()
    {
    }

    public static MultiFactory<T> __CreateDefault()
    {
        return new MultiFactory<T>();
    }
}
