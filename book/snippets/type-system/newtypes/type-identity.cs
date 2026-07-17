using System;

public readonly struct UserId
{
    public readonly int Value;

    public UserId(int value)
    {
        Value = value;
    }

    public static explicit operator UserId(int value) => new UserId(value);
    public static explicit operator int(UserId id) => id.Value;
}
namespace Snippet;

public class Program
{
    private static void Identify(object value)
    {
        if (value is int n)
        {
            Console.WriteLine($"matched int: {n}");
        }
        if (value is UserId id)
        {
            Console.WriteLine($"matched UserId");
        }
    }

    public static void Main(string[] args)
    {
        UserId user = ((UserId)42);
        Identify(user);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
