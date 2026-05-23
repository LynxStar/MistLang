using System;

namespace RefinementTypes;

public class Program
{
    public static void Main(string[] args)
    {
        int a = 20;
        int sum = a + 5;
        int diff = a - 2;
        int computed = a + 10;
        if (computed >= 18)
        {
            int verified = computed;
            Console.WriteLine($"Still adult: {verified}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
