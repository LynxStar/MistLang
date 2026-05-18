using System;

namespace Demo;

public class Program
{
    public static void Main(string[] args)
    {
        int count = 0;
        while (count < 3)
        {
            Console.WriteLine($"Count: {count}");
            count = count + 1;
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
