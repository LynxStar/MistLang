using Standard;

namespace Snippet;

public class Dog
{
    public string Name;

    public Dog(string name)
    {
        this.Name = "";
        Name = name;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Dog dog = new Snippet.Dog("Rex");
        Snippet.Dog same = dog;
        System.Console.WriteLine(same.Name);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
