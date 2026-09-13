using Standard;

namespace Snippet;

public class Tiger : Standard.IClonable<Snippet.Tiger>
{
    private static int made = 0;
    private int stripes;

    public Tiger(int stripes)
    {
        this.stripes = stripes;
    }

    public Snippet.Tiger Clone()
    {
        made = made + 1;
        return new Snippet.Tiger(stripes + made);
    }

    public string Describe()
    {
        return $"stripes={stripes} made={made}";
    }
}

internal class Jaguar : Standard.IClonable<Snippet.Jaguar>
{
    private static int made = 0;
    private int stripes;

    public Jaguar(int stripes)
    {
        this.stripes = stripes;
    }

    public Snippet.Jaguar Clone()
    {
        made = made + 1;
        return new Snippet.Jaguar(stripes + made);
    }

    public string Describe()
    {
        return $"stripes={stripes} made={made}";
    }

    public static explicit operator Snippet.Jaguar(Snippet.Tiger @this)
    {
        Snippet.Jaguar.made = Snippet.Jaguar.made + 1;
        return new Snippet.Jaguar(__In_Snippet_Tiger_stripes(@this) + Snippet.Jaguar.made);
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "stripes")]
    private static extern ref int __In_Snippet_Tiger_stripes(Snippet.Tiger __target);

    public static explicit operator Snippet.Tiger(Snippet.Jaguar @this)
    {
        __Out_Snippet_Tiger_made(null) = __Out_Snippet_Tiger_made(null) + 1;
        return new Snippet.Tiger(@this.stripes + __Out_Snippet_Tiger_made(null));
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.StaticField, Name = "made")]
    private static extern ref int __Out_Snippet_Tiger_made(Snippet.Tiger? __target);
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Tiger tiger = new Snippet.Tiger(10);
        Snippet.Jaguar first = ((Snippet.Jaguar)tiger);
        Snippet.Jaguar second = ((Snippet.Jaguar)tiger);
        Snippet.Tiger back = ((Snippet.Tiger)second);
        System.Console.WriteLine($"first {first.Describe()}");
        System.Console.WriteLine($"second {second.Describe()}");
        System.Console.WriteLine($"back {back.Describe()}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
