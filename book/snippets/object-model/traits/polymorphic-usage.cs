using System;

namespace Traits;

public class Program
{
    private static void PrintItem(Printable item)
    {
        Console.WriteLine($"Name: {item.Name}, Id: {item.Id}");
    }

    public static void Main(string[] args)
    {
        var user = new User("Alice", 42);
        var product = new Product("Widget", 1001);

        // Trait-typed variables
        Printable p1 = user;
        Printable p2 = product;

        // Pass to trait-typed parameter
        PrintItem(user);
        PrintItem(product);
    }
}
