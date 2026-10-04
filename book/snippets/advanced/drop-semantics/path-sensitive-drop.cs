using Standard;

namespace Snippets;

public class Lease
{
    private int Id;

    public Lease(int id)
    {
        this.Id = id;
    }

    public void Drop()
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"Lease({Id}) released"));
    }

    public int GetId()
    {
        return Id;
    }
}

public class Program
{
    public static void Settle(bool early)
    {
        Snippets.Lease lease = new Snippets.Lease(1);
        var __e1 = true;
        try
        {
            if (early)
            {
                MistOwnership.Handed(lease, ref __e1).Drop();
            }
        }
        finally
        {
            if (__e1)
            {
                lease.Drop();
            }
        }
    }

    public static Snippets.Lease KeepOrReplace(bool keep)
    {
        Snippets.Lease lease = new Snippets.Lease(2);
        var __e2 = true;
        try
        {
            if (keep)
            {
                return MistOwnership.Handed(lease, ref __e2);
            }
            return new Snippets.Lease(3);
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
                var __e3 = true;
                try
                {
                    MistOwnership.Handed(renewed, ref __e3).Drop();
                    MistOwnership.Taken(renewed = new Snippets.Lease(6), ref __e3);
                }
                finally
                {
                    if (__e3)
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
