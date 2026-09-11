using System;

namespace Snippet;

public class Entity
{
    private string name;
    private int id;

    public Entity(string name, int id)
    {
        this.name = name;
        this.id = id;
    }

    public string GetName()
    {
        return this.name;
    }

    public int GetId()
    {
        return this.id;
    }

    public string Describe()
    {
        return $"Entity({this.name}, {this.id})";
    }
}

class Actor
{
    private string name;
    private int id;

    public Actor(string name, int id)
    {
        this.name = name;
        this.id = id;
    }

    public string GetName()
    {
        return this.name;
    }

    public int GetId()
    {
        return this.id;
    }

    public string Describe()
    {
        return $"Entity({this.name}, {this.id})";
    }
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Point
{
    public int X;
    public int Y;

    public Point(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    private Point(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        this.X = p0;
        this.Y = p1;
    }

    internal static Point __FromParts(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Point(p0, p1, default(Mist.Emission.CSharp.__StructInit));
    }

    public string Describe()
    {
        return $"({this.X}, {this.Y})";
    }
}

readonly struct UserId
{
    public readonly int Value;

    public UserId(int value)
    {
        Value = value;
    }

    public static explicit operator UserId(int value) => new UserId(value);
    public static explicit operator int(UserId wrapped) => wrapped.Value;
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
struct Position
{
    public int X;
    public int Y;

    public Position(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public string Describe()
    {
        return $"({this.X}, {this.Y})";
    }
}

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
