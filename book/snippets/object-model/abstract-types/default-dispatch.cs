using System;

namespace Snippet;

public interface IGreetable
{
    string Name();

    string Greet()
    {
        return $"Hello, I am {this.Name()}";
    }
}

public class FriendlyDog : Snippet.IGreetable
{
    private string dogName;

    public FriendlyDog(string dogName)
    {
        this.dogName = dogName;
    }

    public string Name()
    {
        return dogName;
    }

    public string Greet()
    {
        return $"Hello, I am {this.Name()}";
    }
}

public class FormalCat : Snippet.IGreetable
{
    private string catName;

    public FormalCat(string catName)
    {
        this.catName = catName;
    }

    public string Name()
    {
        return catName;
    }

    public string Greet()
    {
        return $"Good day. I am {this.Name()}, and I require attention.";
    }
}

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
