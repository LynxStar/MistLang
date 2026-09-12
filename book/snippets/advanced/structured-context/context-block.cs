using Standard;

namespace Snippet;

public class Consumers
{
    public static int GetInt()
    {
        var n = (int)MistContext.Peek<int>();
        return n;
    }

    public Consumers()
    {
    }

    public static Consumers __CreateDefault()
    {
        return new Consumers();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        {
            var count = 10;
            MistContext.Push<int>(count);
            try
            {
                System.Console.WriteLine(Snippet.Consumers.GetInt());
                {
                    var __e1 = 99;
                    MistContext.Push<int>(__e1);
                    try
                    {
                        System.Console.WriteLine(Snippet.Consumers.GetInt());
                    }
                    finally
                    {
                        MistContext.Pop<int>();
                    }
                }
                System.Console.WriteLine(Snippet.Consumers.GetInt());
            }
            finally
            {
                MistContext.Pop<int>();
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
