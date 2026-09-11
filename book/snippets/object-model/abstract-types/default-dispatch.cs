using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.FriendlyDog dog = new Snippet.FriendlyDog("Rex");
        Snippet.FormalCat cat = new Snippet.FormalCat("Whiskers");
        Console.WriteLine(dog.Greet());
        Console.WriteLine(cat.Greet());
        Snippet.IGreetable greeter = dog;
        Console.WriteLine(greeter.Greet());
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
