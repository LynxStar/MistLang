using Standard;
using System;

namespace ContextDemo;

public class Service
{
    public static string Describe()
    {
        var name = (string)MistContext.Peek<string>();
        var count = (int)MistContext.Peek<int>();
        return $"name={name}, count={count}";
    }

    public static Service __CreateDefault()
    {
        var inst = new Service();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var name = "alpha";
        MistContext.Push<string>(name);
        try
        {
            var count = 5;
            MistContext.Push<int>(count);
            try
            {
                var result = Service.Describe();
                System.Console.WriteLine(result);
            }
            finally
            {
                MistContext.Pop<int>();
            }
        }
        finally
        {
            MistContext.Pop<string>();
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
