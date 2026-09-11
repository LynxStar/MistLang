using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int[] values = new int[5];
        values[0] = 3;
        values[1] = 7;
        values[2] = 2;
        values[3] = 9;
        values[4] = 4;
        int sum = 0;
        foreach (int v in values)
        {
            if (v > 5)
            {
                sum = sum + v;
            }
        }
        Console.WriteLine($"Sum of values > 5: {sum}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
