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
        return new Snippet.Tiger($"{Name} II");
    }
}

internal class Jaguar : Snippet.Cat
{
    public Jaguar(string name) : base(name)
    {
    }

    public Snippet.Jaguar Twin()
    {
        return new Snippet.Jaguar($"{Name} II");
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
