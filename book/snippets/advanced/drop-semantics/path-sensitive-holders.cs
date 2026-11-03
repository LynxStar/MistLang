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
        System.Console.WriteLine(System.FormattableString.Invariant($"Lease({this.Id}) released"));
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
        this.First = new Snippets.Lease(1);
        this.Second = new Snippets.Lease(2);
        this.Early = early;
    }

    public void Drop()
    {
        var __e1309 = true;
        try
        {
            try
            {
                if (this.Early)
                {
                    MistOwnership.Handed(this.First, ref __e1309).Drop();
                }
            }
            finally
            {
                if (__e1309)
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
        var __e1310 = true;
        try
        {
            if (early)
            {
                MistOwnership.Handed(lease, ref __e1310).Drop();
            }
        }
        finally
        {
            if (__e1310)
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
            {
                var __e1311 = scoped;
                MistContext.Push<Snippets.Lease>(__e1311);
                try
                {
                    System.Console.WriteLine("inside the with");
                }
                finally
                {
                    MistContext.Pop<Snippets.Lease>();
                    __e1311.Drop();
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
