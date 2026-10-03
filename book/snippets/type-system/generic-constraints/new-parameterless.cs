using Standard;

namespace Snippet;

public class Widget
{
    public int Value;

    public Widget()
    {
        this.Value = 7;
    }

    public static Widget __CreateDefault()
    {
        return new Widget();
    }
}

public class Box<T>
{
    public T Value;

    public Box() : this(default(Mist.Emission.CSharp.__DefaultInit), () => default(T))
    {
    }

    public Box(System.Func<T> __default_T) : this(default(Mist.Emission.CSharp.__DefaultInit), __default_T)
    {
    }

    protected Box(Mist.Emission.CSharp.__DefaultInit marker, System.Func<T> __default_T)
    {
        this.Value = __default_T();
    }

    public static Box<T> __CreateDefault(System.Func<T> __default_T)
    {
        return new Box<T>(default(Mist.Emission.CSharp.__DefaultInit), __default_T);
    }
}

internal class Creator<T>
    where T : new()
{
    private readonly System.Func<T> __construct_T_0;

    public T Create()
    {
        return __construct_T_0();
    }

    public Creator() : this(default(Mist.Emission.CSharp.__DefaultInit), () => new T())
    {
    }

    public Creator(System.Func<T> __construct_T_0) : this(default(Mist.Emission.CSharp.__DefaultInit), __construct_T_0)
    {
    }

    protected Creator(Mist.Emission.CSharp.__DefaultInit marker, System.Func<T> __construct_T_0)
    {
        this.__construct_T_0 = __construct_T_0;
    }

    public static Creator<T> __CreateDefault(System.Func<T> __construct_T_0)
    {
        return new Creator<T>(default(Mist.Emission.CSharp.__DefaultInit), __construct_T_0);
    }
}

internal class Program
{
    public static void Main(string[] args)
    {
        Snippet.Creator<Snippet.Box<Snippet.Widget>> creator = new Snippet.Creator<Snippet.Box<Snippet.Widget>>(() => new Snippet.Box<Snippet.Widget>(() => Snippet.Widget.__CreateDefault()));
        Snippet.Box<Snippet.Widget> box = creator.Create();
        System.Console.WriteLine(System.FormattableString.Invariant($"value={box.Value.Value}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
