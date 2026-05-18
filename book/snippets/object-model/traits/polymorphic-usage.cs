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
        Printable p1 = user;
        Printable p2 = product;
        PrintItem(user);
        PrintItem(product);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
