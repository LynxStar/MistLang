using System;

namespace Snippets;

public class Connection
{
    int Id;

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
        System.Console.WriteLine($"Connection({Id}).Drop()");
    }
}
public class AffineHolder<T>
{
    T Value;

    public AffineHolder(T value)
    {
        this.Value = value;
    }

    public void Drop()
    {
        System.Console.WriteLine("AffineHolder<T>.Drop()");
        ((dynamic)this.Value).Drop();
    }
}
public class RelevantHolder<T>
{
    T Value;

    bool Dropped;

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
        System.Console.WriteLine("RelevantHolder<T>.Drop()");
        ((dynamic)this.Value).Drop();
    }
}
public class Program
{
    public static void Main()
    {
        var __with_0 = new AffineHolder<Connection>(new Connection(1));
        MistContext.Push<AffineHolder<Connection>>(__with_0);
        try
        {
            System.Console.WriteLine("Linear type in affine box");
        }
        finally
        {
            MistContext.Pop<AffineHolder<Connection>>();
            __with_0.Drop();
        }
        var r = new RelevantHolder<Connection>(new Connection(2));
        try
        {
            System.Console.WriteLine("Linear type in relevant box");
        }
        finally
        {
            r.Drop();
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
