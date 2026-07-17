using System;

namespace Snippet;

[Serializable, Obsolete("combined")]
internal class Legacy
{

    public static Legacy __CreateDefault()
    {
        var inst = new Legacy();
        return inst;
    }
}
[Serializable]
[Obsolete("use NewService")]
internal class OldService
{

    public static OldService __CreateDefault()
    {
        var inst = new OldService();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Multiple attributes in one list: [Serializable, Obsolete]");
        Console.WriteLine("Multiple attribute lists: [Serializable] [Obsolete]");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
