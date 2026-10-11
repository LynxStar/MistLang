using Standard;

namespace Snippet;

public class Box<T>
{
    private T item;

    public Box(T item)
    {
        this.item = item;
    }

    public T Get()
    {
        return item;
    }

    public Snippet.Box<T> Again()
    {
        return new Snippet.Box<T>(item);
    }

    public Snippet.Box<string> Label()
    {
        return new Snippet.Box<string>($"<{MistInvariant.Text(item)}>");
    }
}

internal class IntBox
{
    private int item;

    public IntBox(int item)
    {
        this.item = default(int);
        this.item = item;
    }

    public int Get()
    {
        return item;
    }

    public Snippet.IntBox Again()
    {
        return new Snippet.IntBox(item);
    }

    public Snippet.Box<string> Label()
    {
        return new Snippet.Box<string>(System.FormattableString.Invariant($"<{item}>"));
    }
}

internal class Boxed<T>
{
    private T item;

    public Boxed(T item)
    {
        this.item = item;
    }

    public T Get()
    {
        return item;
    }

    public Snippet.Boxed<T> Again()
    {
        return new Snippet.Boxed<T>(item);
    }

    public Snippet.Boxed<string> Label()
    {
        return new Snippet.Boxed<string>($"<{MistInvariant.Text(item)}>");
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.IntBox box = new Snippet.IntBox(5);
        Snippet.IntBox again = box.Again();
        Snippet.Box<string> label = box.Label();
        Snippet.Boxed<bool> boxed = new Snippet.Boxed<bool>(true);
        Snippet.Boxed<string> relabelled = boxed.Label();
        System.Console.WriteLine(System.FormattableString.Invariant($"again={again.Get()} label={label.Get()} relabelled={relabelled.Get()}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
