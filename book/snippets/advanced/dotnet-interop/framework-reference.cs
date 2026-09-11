using System;

namespace FrameworkReference;

public class Program
{
    public static void Main(string[] args)
    {
        Microsoft.AspNetCore.Http.QueryString qs = new Microsoft.AspNetCore.Http.QueryString("?page=1");
        Console.WriteLine($"query={(object?)qs.Value ?? "Void"}");
        Console.WriteLine($"hasValue={qs.HasValue}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
