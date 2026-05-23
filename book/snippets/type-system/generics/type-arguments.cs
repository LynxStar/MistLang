using Standard;
using System;

namespace GenericTypes;

public class Program
{
    public static void Main(string[] args)
    {
        var box = new GenericBox<int>(42);
        System.Console.WriteLine(box.GetItem());
        var result = Utilities.Identity<string>("hello");
        System.Console.WriteLine(result);
        IGenericContainer<int> container = new GenericBox<int>(99);
        System.Console.WriteLine(container.GetItem());
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
