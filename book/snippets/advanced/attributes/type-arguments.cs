using System;
using System.Diagnostics;

namespace Snippet;

[DebuggerTypeProxy(typeof(List<int>))]
public class CustomList
{

    public static CustomList __CreateDefault()
    {
        var inst = new CustomList();
        return inst;
    }
}
[JsonConverter<int>]
public class TypedConfig
{

    public static TypedConfig __CreateDefault()
    {
        var inst = new TypedConfig();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Type literal arg: [DebuggerTypeProxy(List<int>)]");
        Console.WriteLine("Generic type arg: [JsonConverter<int>]");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
