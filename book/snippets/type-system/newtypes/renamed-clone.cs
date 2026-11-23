using Standard;

namespace Snippet;

public class Cat
{
    public string Name;

    public Cat(string name)
    {
        this.Name = "";
        this.Name = name;
    }
}

public class Tiger : Snippet.Cat
{
    public Tiger(string name) : base(name)
    {
    }

    public Snippet.Tiger Twin()
    {
        return new Snippet.Tiger($"{this.Name} II");
    }
}

internal class Jaguar : Snippet.Cat
{
    public Jaguar(string name) : base(name)
    {
    }

    public Snippet.Jaguar Twin()
    {
        return new Snippet.Jaguar($"{this.Name} II");
    }

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull("value")]
    public static explicit operator Snippet.Tiger?(Snippet.Jaguar? value)
    {
        if(value is null)
        {
            return null;
        }

        var copy = (Snippet.Tiger)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Snippet.Tiger));
        __Snippet_Cat_Name(copy) = __Snippet_Cat_Name(value);
        return copy;
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "Name")]
    private static extern ref string __Snippet_Cat_Name(Snippet.Cat target);

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull("value")]
    public static explicit operator Snippet.Jaguar?(Snippet.Tiger? value)
    {
        if(value is null)
        {
            return null;
        }

        var copy = (Snippet.Jaguar)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Snippet.Jaguar));
        __Snippet_Cat_Name(copy) = __Snippet_Cat_Name(value);
        return copy;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Jaguar jaguar = new Snippet.Jaguar("Bagheera");
        Snippet.Jaguar twin = jaguar.Twin();
        Snippet.Cat cat = twin;
        object boxed = twin;
        System.Console.WriteLine($"twin={twin.Name} cat={cat.Name}");
        System.Console.WriteLine($"is Jaguar={boxed is Snippet.Jaguar} is Tiger={boxed is Snippet.Tiger} is Cat={boxed is Snippet.Cat}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
