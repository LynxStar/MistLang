using Standard;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Snippet;

[System.Diagnostics.DebuggerTypeProxyAttribute(typeof(System.Collections.Generic.List<int>))]
internal class CustomList
{
    public CustomList()
    {
    }

    public static CustomList __CreateDefault()
    {
        return new CustomList();
    }
}

public class TaggedAttribute<T> : System.Attribute
{
    public TaggedAttribute()
    {
    }

    public static TaggedAttribute<T> __CreateDefault()
    {
        return new TaggedAttribute<T>();
    }
}

[Snippet.TaggedAttribute<int>]
internal class TypedConfig
{
    public TypedConfig()
    {
    }

    public static TypedConfig __CreateDefault()
    {
        return new TypedConfig();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.WriteLine("Type literal arg: [DebuggerTypeProxy(List<int>)]");
        System.Console.WriteLine("Generic type arg: [Tagged<int>]");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
