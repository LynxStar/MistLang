using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var uid = ((UserId)42);
        Console.WriteLine(uid.Value);
        var pos = new Position(10, 20);
        var x = pos.X;
        var y = pos.Y;
        Console.WriteLine(pos.Describe());
        var actor = new Actor("Hero", 1);
        Console.WriteLine(actor.GetName());
        Console.WriteLine(actor.GetId());
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
