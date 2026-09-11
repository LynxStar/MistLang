using System;

namespace Snippets;

public class Connection
{
    private int Id;

    public Connection(int id)
    {
        this.Id = id;
    }

    public int GetId()
    {
        return Id;
    }

    public void Drop()
    {
        Console.WriteLine($"Connection({Id}).Drop()");
    }
}

public class AffineHolder<T>
{
    private T Value;

    public AffineHolder(T value)
    {
        this.Value = value;
    }

    public void Drop()
    {
        Console.WriteLine("AffineHolder<T>.Drop()");
        ((dynamic)this.Value).Drop();
    }
}

public class RelevantHolder<T>
{
    private T Value;
    private bool Dropped;

    public RelevantHolder(T value)
    {
        this.Value = value;
        this.Dropped = false;
    }

    public void Drop()
    {
        if (Dropped)
        {
            return;
        }
        this.Dropped = true;
        Console.WriteLine("RelevantHolder<T>.Drop()");
        ((dynamic)this.Value).Drop();
    }
}

public class Program
{
    public static void Main()
    {
        {
            var __e1 = new Snippets.AffineHolder<Snippets.Connection>(new Snippets.Connection(1));
            MistContext.Push<Snippets.AffineHolder<Snippets.Connection>>(__e1);
            try
            {
                Console.WriteLine("Linear type in affine box");
            }
            finally
            {
                MistContext.Pop<Snippets.AffineHolder<Snippets.Connection>>();
                __e1.Drop();
            }
        }
        Snippets.RelevantHolder<Snippets.Connection> r = new Snippets.RelevantHolder<Snippets.Connection>(new Snippets.Connection(2));
        try
        {
            Console.WriteLine("Linear type in relevant box");
        }
        finally
        {
            r.Drop();
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
