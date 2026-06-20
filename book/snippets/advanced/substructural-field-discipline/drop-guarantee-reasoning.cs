using System;

namespace Snippets;

public class AffineToken
{
    int Id;

    public AffineToken(int id)
    {
        this.Id = id;
    }

    public void Drop()
    {
        System.Console.WriteLine($"AffineToken({Id}).Drop()");
    }
}
public class PlainData
{
    int Value;

    public PlainData(int v)
    {
        this.Value = v;
    }
}
public class AffineContainer
{
    AffineToken Inner;

    PlainData Data;

    public AffineContainer(int id)
    {
        this.Inner = new AffineToken(id);
        this.Data = new PlainData(id);
    }

    public void Drop()
    {
        System.Console.WriteLine("AffineContainer.Drop()");
        this.Inner.Drop();
    }
}
public class Program
{
    public static void Main()
    {
        var __with_0 = new AffineContainer(1);
        MistContext.Push<AffineContainer>(__with_0);
        try
        {
            System.Console.WriteLine("Inside with block — container will be dropped");
        }
        finally
        {
            MistContext.Pop<AffineContainer>();
            __with_0.Drop();
        }
        System.Console.WriteLine("Done");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
