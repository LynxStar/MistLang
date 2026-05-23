using Standard;
using System;

namespace Newtypes;

public class Program
{
    public static void Main(string[] args)
    {
        var uid = ((UserId)42);
        System.Console.WriteLine(uid.Value);
        var pos = new Position(10, 20);
        int x = pos.X;
        int y = pos.Y;
        System.Console.WriteLine(pos.Describe());
        var actor = new Actor("Hero", 1);
        System.Console.WriteLine(actor.GetName());
        System.Console.WriteLine(actor.GetId());
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
