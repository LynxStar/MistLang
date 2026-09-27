using Standard;

namespace Snippet;

public class UniqueHandle
{
    private int Id;
    private int RefCount;

    public UniqueHandle(int id)
    {
        this.Id = id;
        this.RefCount = 1;
    }

    public int GetId()
    {
        return Id;
    }

    public void Touch()
    {
        this.RefCount = RefCount + 1;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"UniqueHandle(Id={Id}, RefCount={RefCount})");
    }
}

public class OwnedTypeDemo
{
    public static void Run()
    {
        Snippet.UniqueHandle h = new Snippet.UniqueHandle(42);
        h.Touch();
        System.Console.WriteLine($"handle={h.Describe()}");
    }

    public OwnedTypeDemo()
    {
    }

    public static OwnedTypeDemo __CreateDefault()
    {
        return new OwnedTypeDemo();
    }
}
