using Standard;
using System;

namespace Snippet;

[System.SerializableAttribute]
[System.ObsoleteAttribute("combined")]
internal class Legacy
{
    public Legacy()
    {
    }

    public static Legacy __CreateDefault()
    {
        return new Legacy();
    }
}

[System.SerializableAttribute]
[System.ObsoleteAttribute("use NewService")]
internal class OldService
{
    public OldService()
    {
    }

    public static OldService __CreateDefault()
    {
        return new OldService();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.WriteLine("Multiple attributes in one list: [Serializable, Obsolete]");
        System.Console.WriteLine("Multiple attribute lists: [Serializable] [Obsolete]");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
