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

        // Interface-typed variables
        IDescribable describable = person;

        // Pass implementing types to interface parameter
        PrintDescription(person);
        PrintDescription(robot);
    }
}
