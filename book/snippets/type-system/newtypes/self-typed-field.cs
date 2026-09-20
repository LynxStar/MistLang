using Standard;

namespace Snippet;

public class Node : Standard.IClonable<Snippet.Node>
{
    public int Item;
    public Snippet.Node Next;

    public Node(int item, Snippet.Node next)
    {
        this.Item = item;
        this.Next = next;
    }

    public Snippet.Node Clone()
    {
        return new Snippet.Node(this.Item, this.Next?.Clone());
    }
}

public class Chain : Standard.IClonable<Snippet.Chain>
{
    public int Item;
    public Snippet.Chain Next;

    public Chain(int item, Snippet.Chain next)
    {
        this.Item = item;
        this.Next = next;
    }

    public Snippet.Chain Clone()
    {
        return new Snippet.Chain(this.Item, this.Next?.Clone());
    }

    public static explicit operator Snippet.Chain(Snippet.Node @this)
    {
        return new Snippet.Chain(@this.Item, (@this.Next is { } __converted283 ? (Snippet.Chain)(__converted283) : null));
    }
}

public class Program
{
    public static string Spell(Snippet.Chain head)
    {
        string text = "";
        Snippet.Chain cursor = head;
        {
            while (cursor is Snippet.Chain here)
            {
                text = $"{text} {here.Item}";
                cursor = here.Next;
            }
        }
        return text;
    }

    public static void Main(string[] args)
    {
        Snippet.Node end = null;
        Snippet.Node nodes = new Snippet.Node(1, new Snippet.Node(2, end));
        Snippet.Chain chain = ((Snippet.Chain)(nodes));
        {
            if (nodes.Next is Snippet.Node second)
            {
                second.Item = 99;
            }
        }
        System.Console.WriteLine($"chain{Spell(chain)}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
