using Standard;
using System;
using System.Text.RegularExpressions;

namespace Snippet;

public class Tag : IClonable<Tag>
{
    public int Code;

    public Tag(int code)
    {
        this.Code = code;
    }

    public Tag Clone()
    {
        return new Tag(Code);
    }

    public string Describe()
    {
        return $"Tag({Code})";
    }
}
public class IClonableDemo
{
    public static void Run()
    {
        var shared = new Tag(7);
        Tag fresh = shared;
        System.Console.WriteLine($"source={shared.Describe()}, owned clone={fresh.Describe()}");
    }

    public static IClonableDemo __CreateDefault()
    {
        var inst = new IClonableDemo();
        return inst;
    }
}
