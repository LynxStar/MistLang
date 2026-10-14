using Standard;

namespace Snippet;

public class MyList<T>
{
    public int count;
    public T item0;

    public MyList()
    {
        this.count = default(int);
        this.count = 0;
    }

    public int Size()
    {
        return this.count;
    }

    public T GetAt(int index)
    {
        return this.item0;
    }

    public void Add(T value)
    {
        this.item0 = value;
        this.count = this.count + 1;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.MyList<int> ints = new Snippet.MyList<int>();
        ints.Add(42);
        dynamic widened = ints;
        System.Console.WriteLine(System.FormattableString.Invariant($"size: {widened.Size()}"));
        object first = widened.GetAt(0);
        {
            if (first is int number)
            {
                System.Console.WriteLine(System.FormattableString.Invariant($"first: {number}"));
            }
        }
        {
            if (widened is Snippet.MyList<int> narrowed)
            {
                narrowed.Add(7);
                System.Console.WriteLine(System.FormattableString.Invariant($"narrowed size: {narrowed.Size()}"));
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
