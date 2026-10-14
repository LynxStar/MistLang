using Standard;

namespace Snippet;

public class Shelf
{
    private string[] books = new string[] { "Dune", "Emma", "Ivanhoe", "Kim" };

    public int Count
    {
        get
        {
            return 4;
        }
    }

    public string this[int index]
    {
        get
        {
            return books[index];
        }
    }

    public string[] this[System.Range range]
    {
        get
        {
            return books[range];
        }
    }

    public Shelf()
    {
    }

    public static Shelf __CreateDefault()
    {
        return new Shelf();
    }
}

public class Tape
{
    public int Length
    {
        get
        {
            return 10;
        }
    }

    public string Slice(int start, int length)
    {
        return System.FormattableString.Invariant($"start={start} length={length}");
    }

    public Tape()
    {
    }

    public static Tape __CreateDefault()
    {
        return new Tape();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Shelf shelf = new Snippet.Shelf();
        foreach (string book in shelf[1..3])
        {
            System.Console.WriteLine(book);
        }
        Snippet.Tape tape = new Snippet.Tape();
        System.Console.WriteLine(tape[2..5]);
        System.Console.WriteLine(tape[^3..]);
        {
            if (shelf is [var first, .. var rest])
            {
                System.Console.WriteLine(System.FormattableString.Invariant($"{first} then {rest.Length} more"));
            }
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
