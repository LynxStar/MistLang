using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var dog = new FriendlyDog("Rex");
        var cat = new FormalCat("Whiskers");
        Console.WriteLine(dog.Greet());
        Console.WriteLine(cat.Greet());
        IGreetable greeter = dog;
        Console.WriteLine(greeter.Greet());
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
