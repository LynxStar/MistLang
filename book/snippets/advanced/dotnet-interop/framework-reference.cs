using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var qs = new Microsoft.AspNetCore.Http.QueryString("?page=1");
        System.Console.WriteLine($"query={qs.Value}");
        System.Console.WriteLine($"hasValue={qs.HasValue}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
