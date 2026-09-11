using System;

namespace Snippets;

public class Resource
{
    private int Id;

    public Resource(int id)
    {
        this.Id = id;
    }

    public void Drop()
    {
        Console.WriteLine($"Resource({Id}).Drop()");
    }
}

public class Token
{
    private int Id;

    public Token(int id)
    {
        this.Id = id;
    }

    public void Drop()
    {
        Console.WriteLine($"Token({Id}).Drop()");
    }
}

public class Subscription
{
    private int Id;
    private bool Dropped;

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
        Console.WriteLine($"Subscription({Id}).Drop()");
    }
}

public class PlainData
{
    private int Value;

    public PlainData(int v)
    {
        this.Value = v;
    }
}

public class LinearHolder
{
    private Snippets.Resource Lin;
    private Snippets.Token Aff;
    private Snippets.Subscription Rel;
    private Snippets.PlainData Plain;

    public LinearHolder()
    {
        this.Lin = new Snippets.Resource(1);
        this.Aff = new Snippets.Token(2);
        this.Rel = new Snippets.Subscription(3);
        this.Plain = new Snippets.PlainData(4);
    }

    public void Drop()
    {
        Console.WriteLine("LinearHolder.Drop()");
        this.Lin.Drop();
        this.Aff.Drop();
        this.Rel.Drop();
    }
}

public class Program
{
    public static void Main()
    {
        Snippets.LinearHolder holder = new Snippets.LinearHolder();
        try
        {
            Console.WriteLine("Linear container holds all four disciplines:");
        }
        finally
        {
            holder.Drop();
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
