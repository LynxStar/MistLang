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

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Box<Snippet.Widget> constructed = new Snippet.Box<Snippet.Widget>(() => Snippet.Widget.__CreateDefault());
        Snippet.Box<Snippet.Widget> defaulted = Snippet.Box<Snippet.Widget>.__CreateDefault(() => Snippet.Widget.__CreateDefault());
        System.Console.WriteLine(System.FormattableString.Invariant($"new={constructed.Value.Value} default={defaulted.Value.Value}"));
        constructed.Value.Value = 9;
        System.Console.WriteLine(System.FormattableString.Invariant($"independent={defaulted.Value.Value}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
