using System;

namespace GenericTypes;

public class Program
{
    public static void Main(string[] args)
    {
        var box = new GenericBox<int>(42);
        Console.WriteLine(box.GetItem());
        var result = Utilities.Identity<string>("hello");
        Console.WriteLine(result);
        IGenericContainer<int> container = new GenericBox<int>(99);
        Console.WriteLine(container.GetItem());
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
