using Standard;
using System.Collections.ObjectModel;

namespace Snippet;

public class Loud : System.Collections.ObjectModel.Collection<int>
{
    public int inserts;

    protected override void InsertItem(int index, int item)
    {
        this.inserts = this.inserts + 1;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Loud loud = new Snippet.Loud();
        loud.Add(1);
        System.Collections.ObjectModel.Collection<int> collection = loud;
        collection.Add(2);
        System.Console.WriteLine(System.FormattableString.Invariant($"inserts={loud.inserts} count={loud.Count}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
