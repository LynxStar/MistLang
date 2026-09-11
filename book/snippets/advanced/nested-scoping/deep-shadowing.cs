using System;

namespace Snippet;

public class DeepShadowing
{
    public static void Main(string[] args)
    {
        string name = "first";
        {
            string name_1 = "second";
            {
                string name_2 = "third";
                Console.WriteLine(name_2);
            }
            Console.WriteLine(name_1);
        }
        Console.WriteLine(name);
    }

    public DeepShadowing()
    {
    }

    public static DeepShadowing __CreateDefault()
    {
        return new DeepShadowing();
    }
}
