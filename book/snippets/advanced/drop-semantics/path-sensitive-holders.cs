using Standard;

namespace Snippets;

public class Lease
{
    private int Id;

    public Lease(int id)
    {
        this.Id = default(int);
        this.Id = id;
    }

    public void Drop()
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"Lease({Id}) released"));
    }
}

public class Pair
{
    private Snippets.Lease First;
    private Snippets.Lease Second;
    private bool Early;

    public Pair(bool early)
    {
        this.Early = default(bool);
        First = new Snippets.Lease(1);
        Second = new Snippets.Lease(2);
        Early = early;
    }

    public void Drop()
    {
        var __e1 = true;
        try
        {
            try
            {
                if (Early)
                {
                    MistOwnership.Handed(First, ref __e1).Drop();
                }
            }
            finally
            {
                if (__e1)
                {
                    this.First.Drop();
                }
            }
        }
        finally
        {
            this.Second.Drop();
        }
    }
}

public class Program
{
    public static void Settle(Snippets.Lease lease, bool early)
    {
        var __e2 = true;
        try
        {
            if (early)
            {
                MistOwnership.Handed(lease, ref __e2).Drop();
            }
        }
        finally
        {
            if (__e2)
            {
                lease.Drop();
            }
        }
    }

    public static void Main()
    {
        Settle(new Snippets.Lease(3), false);
        Settle(new Snippets.Lease(4), true);
        Snippets.Pair pair = new Snippets.Pair(true);
        try
        {
            Snippets.Lease scoped = new Snippets.Lease(5);
            var __e3 = true;
            try
            {
                {
                    var __e4 = scoped;
                    MistContext.Push<Snippets.Lease>(__e4);
                    try
                    {
                        System.Console.WriteLine("inside the with");
                    }
                    finally
                    {
                        MistContext.Pop<Snippets.Lease>();
                        if (__e3)
                        {
                            __e3 = false;
                            scoped.Drop();
                        }
                    }
                }
                {
                    Snippets.Lease counter = new Snippets.Lease(6);
                    try
                    {
                        for (; true; )
                        {
                            System.Console.WriteLine("loop body");
                            break;
                        }
                    }
                    finally
                    {
                        counter.Drop();
                    }
                }
            }
            finally
            {
                if (__e3)
                {
                    scoped.Drop();
                }
            }
        }
        finally
        {
            pair.Drop();
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
