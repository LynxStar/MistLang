using Standard;

namespace Snippet;

public class Payload
{
    public int Size;

    public Payload(int size)
    {
        this.Size = default(int);
        this.Size = size;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"Payload({Size})");
    }
}

public class Envelope
{
    private Snippet.Payload Body;
    private int Stamp;

    public Envelope(Snippet.Payload body)
    {
        this.Stamp = default(int);
        this.Body = body;
        this.Stamp = 1;
    }

    public void Reseal()
    {
        this.Body = new Snippet.Payload(0);
        this.Stamp = Stamp + 1;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"Envelope(Body={Body.Describe()}, Stamp={Stamp})");
    }
}

public class CooperationContractDemo
{
    public static void Run()
    {
        Snippet.Payload body = new Snippet.Payload(256);
        Snippet.Envelope e = new Snippet.Envelope(body);
        e.Reseal();
        System.Console.WriteLine($"contract={e.Describe()}");
    }

    public CooperationContractDemo()
    {
    }

    public static CooperationContractDemo __CreateDefault()
    {
        return new CooperationContractDemo();
    }
}
