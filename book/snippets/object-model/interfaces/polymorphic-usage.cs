using System;

namespace Interfaces;

public class Program
{
    private static void PrintDescription(IDescribable item)
    {
        Console.WriteLine(item.Describe());
    }

    public static void Main(string[] args)
    {
        var person = new Person("Alice", 30);
        var robot = new Robot("T-800", 2);
        IDescribable describable = person;
        PrintDescription(person);
        PrintDescription(robot);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
