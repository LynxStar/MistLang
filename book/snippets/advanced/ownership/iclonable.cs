using Standard;

namespace Snippet;

public class Tag : Standard.IClonable<Snippet.Tag>
{
    public int Code;

    public Tag(int code)
    {
        this.Code = default(int);
        this.Code = code;
    }

    public Snippet.Tag Clone()
    {
        return new Snippet.Tag(Code);
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"Tag({Code})");
    }
}

public class IClonableDemo
{
    public static void Run()
    {
        Snippet.Tag shared = new Snippet.Tag(7);
        Snippet.Tag fresh = shared.Clone();
        System.Console.WriteLine($"source={shared.Describe()}, owned clone={fresh.Describe()}");
    }

    public IClonableDemo()
    {
    }

    public static IClonableDemo __CreateDefault()
    {
        return new IClonableDemo();
    }
}
