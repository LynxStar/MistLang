using System;

namespace ContextDemo;

public class Consumers
{
    public static int GetInt()
    {
        var n = (int)MistContext.Peek<int>();
        return n;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        var count = 10;
        MistContext.Push<int>(count);
        try
        {
            Console.WriteLine(Consumers.GetInt());

            MistContext.Push<int>(99);
            try
            {
                Console.WriteLine(Consumers.GetInt());
            }
            finally
            {
                MistContext.Pop<int>();
            }

            Console.WriteLine(Consumers.GetInt());
        }
        finally
        {
            MistContext.Pop<int>();
        }
    }
}
