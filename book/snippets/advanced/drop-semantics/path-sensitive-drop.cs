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

    public int GetId()
    {
        return this.Id;
    }
}

public class Program
{
    public static void Settle(bool early)
    {
        Snippets.Lease lease = new Snippets.Lease(1);
        var __e1309 = true;
        try
        {
            if (early)
            {
                MistOwnership.Handed(lease, ref __e1309).Drop();
            }
        }
        finally
        {
            if (__e1309)
            {
                lease.Drop();
            }
        }
    }

    public static Snippets.Lease KeepOrReplace(bool keep)
    {
        Snippets.Lease lease = new Snippets.Lease(2);
        var __e1310 = true;
        try
        {
            if (keep)
            {
                return MistOwnership.Handed(lease, ref __e1310);
            }
            return new Snippets.Lease(3);
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
        Snippets.Lease first = new Snippets.Lease(4);
        Snippets.Lease second = first;
        try
        {
            Settle(false);
            Settle(true);
            Snippets.Lease kept = KeepOrReplace(false);
            try
            {
                System.Console.WriteLine(System.FormattableString.Invariant($"Kept Lease({kept.GetId()})"));
                Snippets.Lease renewed = new Snippets.Lease(5);
                var __e1311 = true;
                try
                {
                    MistOwnership.Handed(renewed, ref __e1311).Drop();
                    MistOwnership.Taken(renewed = new Snippets.Lease(6), ref __e1311);
                }
                finally
                {
                    if (__e1311)
                    {
                        renewed.Drop();
                    }
                }
            }
            finally
            {
                kept.Drop();
            }
        }
        finally
        {
            second.Drop();
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
