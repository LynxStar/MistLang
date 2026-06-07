using System;

namespace Snippet;

public class Node
{
    public int Id;

    public Node(int id)
    {
        this.Id = id;
    }

    public string Describe()
    {
        return $"Node({Id})";
    }

    protected Node()
    {
        this.Id = 0;
    }

    public static Node __CreateDefault()
    {
        var inst = new Node();
        return inst;
    }
}
public class Slot
{
    public Node Current;

    public int Count;

    public Slot(Node first)
    {
        this.Current = first;
        this.Count = 1;
    }

    public string Describe()
    {
        return $"Slot(Current={Current.Describe()}, Count={Count})";
    }

    protected Slot()
    {
        this.Current = Node.__CreateDefault();
        this.Count = 0;
    }

    public static Slot __CreateDefault()
    {
        var inst = new Slot();
        return inst;
    }
}
public class FieldProvenanceDemo
{
    public static void Run()
    {
        Slot s = new Slot(new Node(1));
        s.Current = new Node(2);
        s.Count = 2;
        System.Console.WriteLine($"provenance={s.Describe()}");
    }

    public static FieldProvenanceDemo __CreateDefault()
    {
        var inst = new FieldProvenanceDemo();
        return inst;
    }
}
