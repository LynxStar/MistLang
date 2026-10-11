using Standard;

namespace Snippets;

public class AffineToken
{
    private int Id;

    public AffineToken(int id)
    {
        this.Id = default(int);
        this.Id = id;
    }

    public void Drop()
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"AffineToken({Id}).Drop()"));
    }
}

public class PlainData
{
    private int Value;

    public PlainData(int v)
    {
        this.Value = default(int);
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
        try
        {
            System.Console.WriteLine("AffineContainer.Drop()");
        }
        finally
        {
            this.Inner.Drop();
        }
    }
}

public class Program
{
    public static void Main()
    {
        {
            var __e1 = new Snippets.AffineContainer(1);
            MistContext.Push<Snippets.AffineContainer>(__e1);
            try
            {
                System.Console.WriteLine("Inside with block — container will be dropped");
            }
            finally
            {
                MistContext.Pop<Snippets.AffineContainer>();
                __e1.Drop();
            }
        }
        System.Console.WriteLine("Done");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
