using Standard;

namespace Snippet;

public class Pair
{
    public int X;
    public int Y;

    public Pair(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public void SetX(int x)
    {
        this.X = x;
    }

    public string Describe()
    {
        return $"Pair({X}, {Y})";
    }
}

public class Program
{
    public static void MutateBorrowed(Snippet.Pair p)
    {
        p.SetX(999);
        System.Console.WriteLine($"mutated={p.Describe()}");
    }

    public static void Main()
    {
        Snippet.Pair p = new Snippet.Pair(1, 2);
        MutateBorrowed(p);
        System.Console.WriteLine($"owner after={p.Describe()}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
