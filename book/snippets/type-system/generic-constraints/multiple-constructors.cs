using Standard;

namespace Snippet;

internal class MultiFactory<T>
    where T : Mist.Emission.CSharp.__IConstruct_int<T>, Mist.Emission.CSharp.__IConstruct_bool<T>, new()
{
    private readonly System.Func<T> __construct_T_0;
    private readonly System.Func<int, T> __construct_T_1;
    private readonly System.Func<bool, T> __construct_T_2;

    public T CreateDefault()
    {
        return __construct_T_0();
    }

    public T CreateFromInt(int value)
    {
        return __construct_T_1(value);
    }

    public T CreateFromBool(bool flag)
    {
        return __construct_T_2(flag);
    }

    public MultiFactory() : this(default(Mist.Emission.CSharp.__DefaultInit), () => new T(), (__arg0_0) => T.__New(__arg0_0), (__arg0_0) => T.__New(__arg0_0))
    {
    }

    public MultiFactory(System.Func<T> __construct_T_0, System.Func<int, T> __construct_T_1, System.Func<bool, T> __construct_T_2) : this(default(Mist.Emission.CSharp.__DefaultInit), __construct_T_0, __construct_T_1, __construct_T_2)
    {
    }

    protected MultiFactory(Mist.Emission.CSharp.__DefaultInit marker, System.Func<T> __construct_T_0, System.Func<int, T> __construct_T_1, System.Func<bool, T> __construct_T_2)
    {
        this.__construct_T_0 = __construct_T_0;
        this.__construct_T_1 = __construct_T_1;
        this.__construct_T_2 = __construct_T_2;
    }

    public static MultiFactory<T> __CreateDefault(System.Func<T> __construct_T_0, System.Func<int, T> __construct_T_1, System.Func<bool, T> __construct_T_2)
    {
        return new MultiFactory<T>(default(Mist.Emission.CSharp.__DefaultInit), __construct_T_0, __construct_T_1, __construct_T_2);
    }
}
