using Standard;

namespace Snippet;

public interface IReader
{
    string Read()
    {
        var s = (string)MistContext.Peek<string>();
        return s;
    }
}

public class Reader : Snippet.IReader
{
    public Reader()
    {
    }

    public static Reader __CreateDefault()
    {
        return new Reader();
    }

    public string Read()
    {
        var s = (string)MistContext.Peek<string>();
        return s;
    }
}

public class Program
{
    public static void Main()
    {
        {
            var __e1309 = 7;
            MistContext.Push<int>(__e1309);
            try
            {
                {
                    var __e1310 = "context";
                    MistContext.Push<string>(__e1310);
                    try
                    {
                        System.Console.WriteLine(Snippet.StringExtensions_methodentry_0.Describe("x"));
                        Snippet.Reader reader = new Snippet.Reader();
                        Snippet.IReader view = reader;
                        System.Console.WriteLine(view.Read());
                        System.Console.WriteLine(reader.Read());
                    }
                    finally
                    {
                        MistContext.Pop<string>();
                    }
                }
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

public static class StringExtensions_methodentry_0
{
    public static string Describe(this string self)
    {
        var n = (int)MistContext.Peek<int>();
        return System.FormattableString.Invariant($"{self}:{n}");
    }
}
