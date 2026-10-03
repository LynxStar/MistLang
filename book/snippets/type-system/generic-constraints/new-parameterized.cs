using Standard;

namespace Snippet;

internal class IntFactory<T>
    where T : Mist.Emission.CSharp.__IConstruct_int<T>
{
    private readonly System.Func<int, T> __construct_T_0;

    public T Create(int value)
    {
        return __construct_T_0(value);
    }

    public IntFactory() : this(default(Mist.Emission.CSharp.__DefaultInit), (__arg0_0) => T.__New(__arg0_0))
    {
    }

    public IntFactory(System.Func<int, T> __construct_T_0) : this(default(Mist.Emission.CSharp.__DefaultInit), __construct_T_0)
    {
    }

    protected IntFactory(Mist.Emission.CSharp.__DefaultInit marker, System.Func<int, T> __construct_T_0)
    {
        this.__construct_T_0 = __construct_T_0;
    }

    public static IntFactory<T> __CreateDefault(System.Func<int, T> __construct_T_0)
    {
        return new IntFactory<T>(default(Mist.Emission.CSharp.__DefaultInit), __construct_T_0);
    }
}
