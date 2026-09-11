using System;

namespace Snippet;

[System.SerializableAttribute]
[System.ObsoleteAttribute("combined")]
class Legacy
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
class OldService
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
        Console.WriteLine("Multiple attributes in one list: [Serializable, Obsolete]");
        Console.WriteLine("Multiple attribute lists: [Serializable] [Obsolete]");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
