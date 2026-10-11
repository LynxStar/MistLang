using Standard;

namespace PurityTypes;

public class Tally
{
    private int total;

    public Tally(int start)
    {
        this.total = default(int);
        total = start;
    }

    public int Total
    {
        get
        {
            return total;
        }
    }

    public void Add(int n)
    {
        total = total + n;
    }
}

public class Money
{
    public readonly int Cents;

    public Money(int cents)
    {
        this.Cents = default(int);
        Cents = cents;
    }

    public PurityTypes.Money Plus(PurityTypes.Money other)
    {
        return new PurityTypes.Money(Cents + other.Cents);
    }
}

public class Program
{
    public static int Counted(int a, int b)
    {
        PurityTypes.Tally tally = new PurityTypes.Tally(10);
        tally.Add(a);
        tally.Add(b);
        return tally.Total;
    }

    public static int Combined(PurityTypes.Money a, PurityTypes.Money b)
    {
        return a.Plus(b).Cents;
    }

    public static void Main(string[] args)
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"Counted(3, 4)={Counted(3, 4)}"));
        PurityTypes.Money price = new PurityTypes.Money(250);
        PurityTypes.Money tax = new PurityTypes.Money(20);
        System.Console.WriteLine(System.FormattableString.Invariant($"Combined(price, tax)={Combined(price, tax)}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
