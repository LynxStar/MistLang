using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.FriendlyDog dog = new Snippet.FriendlyDog("Rex");
        Snippet.FormalCat cat = new Snippet.FormalCat("Whiskers");
        System.Console.WriteLine(dog.Greet());
        System.Console.WriteLine(cat.Greet());
        Snippet.IGreetable greeter = dog;
        System.Console.WriteLine(greeter.Greet());
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
