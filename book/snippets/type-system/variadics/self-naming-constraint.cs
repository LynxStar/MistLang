using Standard;

namespace Snippet;

public interface IRank<T>
{
    int Rank();
}

public class Knight : Snippet.IRank<Snippet.Knight>
{
    public int Rank()
    {
        return 3;
    }

    public Knight()
    {
    }

    public static Knight __CreateDefault()
    {
        return new Knight();
    }
}

public class Rook : Snippet.IRank<Snippet.Rook>
{
    public int Rank()
    {
        return 5;
    }

    public Rook()
    {
    }

    public static Rook __CreateDefault()
    {
        return new Rook();
    }
}

public class Program
{
    public static void Main(string[] cmdArgs)
    {
        Ladder_2<Snippet.Knight, Snippet.Rook> ladder = new Ladder_2<Snippet.Knight, Snippet.Rook>();
        System.Console.WriteLine(System.FormattableString.Invariant($"width={ladder.Width()}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
