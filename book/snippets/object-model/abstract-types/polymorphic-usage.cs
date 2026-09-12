using Standard;

namespace Snippet;

public class Person : Snippet.IDescribable
{
    private string name;
    private int age;

    public Person(string name, int age)
    {
        this.name = name;
        this.age = age;
    }

    public string Describe()
    {
        return $"Person: {name}, age {age}";
    }
}

public class Program
{
    private static void PrintDescription(Snippet.IDescribable item)
    {
        System.Console.WriteLine(item.Describe());
    }

    public static void Main(string[] args)
    {
        Snippet.Person person = new Snippet.Person("Alice", 30);
        Snippet.Robot robot = new Snippet.Robot("T-800", 2);
        Snippet.IDescribable describable = person;
        PrintDescription(person);
        PrintDescription(robot);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
