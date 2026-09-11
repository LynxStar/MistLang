using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        UserId uid = ((UserId)42);
        Console.WriteLine(uid.Value);
        Position pos = new Position(10, 20);
        int x = pos.X;
        int y = pos.Y;
        Console.WriteLine(pos.Describe());
        Actor actor = new Actor("Hero", 1);
        Console.WriteLine(actor.GetName());
        Console.WriteLine(actor.GetId());
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
