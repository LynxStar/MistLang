using System;

interface IDescribable
{
    string Describe();
}
namespace Snippet;

public class Person : IDescribable
{
    private string name;

    public Person(string name)
    {
        this.name = name;
    }

    public string Describe()
    {
        return $"Person: {this.name}";
    }
}
public class Robot : IDescribable
{
    private string model;

    public Robot(string model)
    {
        this.model = model;
    }

    public string Describe()
    {
        return $"Robot: {this.model}";
    }
}
public class Program
{
    private static void PrintDescription(IDescribable item)
    {
        Console.WriteLine(item.Describe());
    }

    public static void Main(string[] args)
    {
        var person = new Person("Alice");
        var robot = new Robot("T-800");
        IDescribable describable = person;
        Console.WriteLine(describable.Describe());
        PrintDescription(person);
        PrintDescription(robot);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
