using Standard;

namespace Snippet;

internal interface IPrintable
{
    string Print();
}

internal class PrintableCreator<T>
    where T : class, Snippet.IPrintable, new()
{
    private readonly System.Func<T> __construct_T_0;

    public T CreateNew()
    {
        return __construct_T_0();
    }

    public PrintableCreator() : this(default(Mist.Emission.CSharp.__DefaultInit), () => new T())
    {
    }

    public PrintableCreator(System.Func<T> __construct_T_0) : this(default(Mist.Emission.CSharp.__DefaultInit), __construct_T_0)
    {
    }

    protected PrintableCreator(Mist.Emission.CSharp.__DefaultInit marker, System.Func<T> __construct_T_0)
    {
        this.__construct_T_0 = __construct_T_0;
    }

    public static PrintableCreator<T> __CreateDefault(System.Func<T> __construct_T_0)
    {
        return new PrintableCreator<T>(default(Mist.Emission.CSharp.__DefaultInit), __construct_T_0);
    }
}
