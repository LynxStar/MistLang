using Standard;
using System;

namespace ContextDemo;

public class Consumers
{
    public static int GetInt()
    {
        var n = (int)MistContext.Peek<int>();
        return n;
    }

    public static Consumers __CreateDefault()
    {
        var inst = new Consumers();
        return inst;
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
            System.Console.WriteLine(Consumers.GetInt());
            MistContext.Push<int>(99);
            try
            {
                System.Console.WriteLine(Consumers.GetInt());
            }
            finally
            {
                MistContext.Pop<int>();
            }
            System.Console.WriteLine(Consumers.GetInt());
        }
        finally
        {
            MistContext.Pop<int>();
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
