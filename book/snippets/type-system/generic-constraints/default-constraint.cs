using Standard;

namespace Snippet;

public sealed record class Direction(string Value)
{
    public static readonly Direction North = new Direction("N");
    public static readonly Direction South = new Direction("S");

    public static implicit operator string(Direction e) => e.Value;

    public static explicit operator Direction?(string value)
    {
        if(Equals(value, Direction.North.Value)) return Direction.North;
        if(Equals(value, Direction.South.Value)) return Direction.South;
        return null;
    }

    public override string ToString()
    {
        if(ReferenceEquals(this, Direction.North)) return "North";
        if(ReferenceEquals(this, Direction.South)) return "South";
        return Value.ToString();
    }

    public static Direction __CreateDefault() => Direction.North;
}

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
    private readonly System.Func<T> __default_T;

    public T Get()
    {
        return __default_T();
    }

    public Box() : this(default(Mist.Emission.CSharp.__DefaultInit), () => default(T) ?? throw new System.InvalidOperationException("A semantic default witness is required when the CLR default is null."))
    {
    }

    public Box(System.Func<T> __default_T) : this(default(Mist.Emission.CSharp.__DefaultInit), __default_T)
    {
    }

    protected Box(Mist.Emission.CSharp.__DefaultInit marker, System.Func<T> __default_T)
    {
        this.__default_T = __default_T;
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
        Snippet.Box<int> numbers = new Snippet.Box<int>(() => default(int));
        Snippet.Box<string> text = new Snippet.Box<string>(() => "");
        Snippet.Box<Snippet.Direction> directions = new Snippet.Box<Snippet.Direction>(() => Snippet.Direction.__CreateDefault());
        Snippet.Box<Snippet.Widget> widgets = new Snippet.Box<Snippet.Widget>(() => Snippet.Widget.__CreateDefault());
        System.Console.WriteLine(System.FormattableString.Invariant($"number={numbers.Get()} text='{text.Get()}' direction={directions.Get()}"));
        Snippet.Widget first = widgets.Get();
        first.Value = 9;
        System.Console.WriteLine(System.FormattableString.Invariant($"fresh={widgets.Get().Value}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
