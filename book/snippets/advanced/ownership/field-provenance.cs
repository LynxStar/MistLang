using Standard;

namespace Snippet;

public class Node
{
    public int Id;

    public Node(int id)
    {
        this.Id = default(int);
        this.Id = id;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"Node({Id})");
    }
}

public class Slot
{
    public Snippet.Node Current;
    public int Count;

    public Slot(Snippet.Node first)
    {
        this.Count = default(int);
        this.Current = first;
        this.Count = 1;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"Slot(Current={Current.Describe()}, Count={Count})");
    }
}

public class FieldProvenanceDemo
{
    public static void Run()
    {
        Snippet.Slot s = new Snippet.Slot(new Snippet.Node(1));
        s.Current = new Snippet.Node(2);
        s.Count = 2;
        System.Console.WriteLine($"provenance={s.Describe()}");
    }

    public FieldProvenanceDemo()
    {
    }

    public static FieldProvenanceDemo __CreateDefault()
    {
        return new FieldProvenanceDemo();
    }
}
