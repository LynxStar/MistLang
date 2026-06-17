using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var values = new int[5];
        values[0] = 3;
        values[1] = 7;
        values[2] = 2;
        values[3] = 9;
        values[4] = 4;
        var sum = 0;
        foreach (int v in values)
        {
            if (v > 5)
            {
                sum = sum + v;
            }
        }
        Console.WriteLine($"Sum of values > 5: {sum}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
