using System;

namespace Snippet;

interface IGenericContainer<T>
{
    T GetItem();
}

public class GenericBox<T> : Snippet.IGenericContainer<T>
{
    private T item;

    public GenericBox(T item)
    {
        this.item = item;
    }

    public T GetItem()
    {
        return item;
    }

    public string Describe()
    {
        return $"GenericBox({item})";
    }
}

public class Utilities
{
    public static T Identity<T>(T value)
    {
        return value;
    }

    public static void PrintItem<T>(T item)
    {
        Console.WriteLine($"Item: {item}");
    }

    public Utilities()
    {
    }

    public static Utilities __CreateDefault()
    {
        return new Utilities();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.GenericBox<int> box = new Snippet.GenericBox<int>(42);
        Console.WriteLine(box.GetItem());
        string result = Snippet.Utilities.Identity<string>("hello");
        Console.WriteLine(result);
        Snippet.IGenericContainer<int> container = new Snippet.GenericBox<int>(99);
        Console.WriteLine(container.GetItem());
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
