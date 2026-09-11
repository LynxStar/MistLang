using System;

namespace Snippets;

public class AffineToken
{
    private int Id;

    public AffineToken(int id)
    {
        this.Id = id;
    }

    public void Drop()
    {
        Console.WriteLine($"AffineToken({Id}).Drop()");
    }
}

public class PlainData
{
    private int Value;

    public PlainData(int v)
    {
        this.Value = v;
    }
}

public class AffineContainer
{
    private Snippets.AffineToken Inner;
    private Snippets.PlainData Data;

    public AffineContainer(int id)
    {
        this.Inner = new Snippets.AffineToken(id);
        this.Data = new Snippets.PlainData(id);
    }

    public void Drop()
    {
        Console.WriteLine("AffineContainer.Drop()");
        this.Inner.Drop();
    }
}

public class Program
{
    public static void Main()
    {
        {
            var __e6 = new Snippets.AffineContainer(1);
            MistContext.Push<Snippets.AffineContainer>(__e6);
            try
            {
                Console.WriteLine("Inside with block — container will be dropped");
            }
            finally
            {
                MistContext.Pop<Snippets.AffineContainer>();
                __e6.Drop();
            }
        }
        Console.WriteLine("Done");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
