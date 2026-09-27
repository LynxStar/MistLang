using Standard;

namespace Snippet;

public class Service
{
    public static string Describe()
    {
        var name = (string)MistContext.Peek<string>();
        var count = (int)MistContext.Peek<int>();
        return System.FormattableString.Invariant($"name={name}, count={count}");
    }

    public Service()
    {
    }

    public static Service __CreateDefault()
    {
        return new Service();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        {
            var name = "alpha";
            MistContext.Push<string>(name);
            try
            {
                {
                    var count = 5;
                    MistContext.Push<int>(count);
                    try
                    {
                        string result = Snippet.Service.Describe();
                        System.Console.WriteLine(result);
                    }
                    finally
                    {
                        MistContext.Pop<int>();
                    }
                }
            }
            finally
            {
                MistContext.Pop<string>();
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
