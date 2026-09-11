using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        (int, object) source = (7, "seven");
        (int num, object data) = source;
        Console.WriteLine($"num={num}");
        {
            if (data is string ds)
            {
                Console.WriteLine($"data narrowed to string: {ds}");
            }
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
