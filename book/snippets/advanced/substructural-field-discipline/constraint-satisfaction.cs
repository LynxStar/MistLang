using Standard;

namespace Snippets;

public class Connection
{
    private int Id;

    public Connection(int id)
    {
        this.Id = default(int);
        this.Id = id;
    }

    public int GetId()
    {
        return Id;
    }

    public void Drop()
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"Connection({Id}).Drop()"));
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
        try
        {
            System.Console.WriteLine("AffineHolder<T>.Drop()");
        }
        finally
        {
            ((dynamic)this.Value).Drop();
        }
    }
}

public class RelevantHolder<T>
{
    private T Value;
    private bool Dropped;

    public RelevantHolder(T value)
    {
        this.Dropped = default(bool);
        this.Value = value;
        this.Dropped = false;
    }

    public void Drop()
    {
        try
        {
            if (Dropped)
            {
                return;
            }
            this.Dropped = true;
            System.Console.WriteLine("RelevantHolder<T>.Drop()");
        }
        finally
        {
            ((dynamic)this.Value).Drop();
        }
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
                System.Console.WriteLine("Linear type in affine box");
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
            System.Console.WriteLine("Linear type in relevant box");
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
