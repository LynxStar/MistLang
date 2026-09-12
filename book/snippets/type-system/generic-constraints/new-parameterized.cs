using Standard;

namespace Snippet;

internal class IntFactory<T>
    where T : Mist.Emission.CSharp.__IConstruct_int<T>
{
    public T Create(int value)
    {
        return T.__New(value);
    }

    public IntFactory()
    {
    }

    public static IntFactory<T> __CreateDefault()
    {
        return new IntFactory<T>();
    }
}
