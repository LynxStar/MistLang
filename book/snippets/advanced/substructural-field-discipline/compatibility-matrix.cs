using System;

namespace Snippets;

private class Resource
{
    int Id;

    public Resource(int id)
    {
        this.Id = id;
    }

    public void Drop()
    {
        System.Console.WriteLine($"Resource({Id}).Drop()");
    }
}
private class Token
{
    int Id;

    public Token(int id)
    {
        this.Id = id;
    }

    public void Drop()
    {
        System.Console.WriteLine($"Token({Id}).Drop()");
    }
}
private class Subscription
{
    int Id;

    bool Dropped;

    public Subscription(int id)
    {
        this.Id = id;
        this.Dropped = false;
    }

    public void Drop()
    {
        if (Dropped)
        {
            return;
        }
        this.Dropped = true;
        System.Console.WriteLine($"Subscription({Id}).Drop()");
    }
}
private class PlainData
{
    int Value;

    public PlainData(int v)
    {
        this.Value = v;
    }
}
public class LinearHolder
{
    Resource Lin;

    Token Aff;

    Subscription Rel;

    PlainData Plain;

    public LinearHolder()
    {
        this.Lin = new Resource(1);
        this.Aff = new Token(2);
        this.Rel = new Subscription(3);
        this.Plain = new PlainData(4);
    }

    public void Drop()
    {
        System.Console.WriteLine("LinearHolder.Drop()");
        this.Lin.Drop();
        this.Aff.Drop();
        this.Rel.Drop();
    }
}
public class Program
{
    public static void Main()
    {
        var holder = new LinearHolder();
        try
        {
            System.Console.WriteLine("Linear container holds all four disciplines:");
        }
        finally
        {
            holder.Drop();
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
