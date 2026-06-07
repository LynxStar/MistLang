using System;
using System.Text.RegularExpressions;

namespace Snippet;

public class Payload
{
    public int Size;

    public Payload(int size)
    {
        this.Size = size;
    }

    public string Describe()
    {
        return $"Payload({Size})";
    }

    protected Payload()
    {
        this.Size = 0;
    }

    public static Payload __CreateDefault()
    {
        var inst = new Payload();
        return inst;
    }
}
public class Envelope
{
    Payload Body;

    int Stamp;

    public Envelope(Payload body)
    {
        this.Body = body;
        this.Stamp = 1;
    }

    public void Reseal()
    {
        this.Body = new Payload(0);
        this.Stamp = Stamp + 1;
    }

    public string Describe()
    {
        return $"Envelope(Body={Body.Describe()}, Stamp={Stamp})";
    }

    protected Envelope()
    {
        this.Body = Payload.__CreateDefault();
        this.Stamp = 0;
    }

    public static Envelope __CreateDefault()
    {
        var inst = new Envelope();
        return inst;
    }
}
public class CooperationContractDemo
{
    public static void Run()
    {
        Payload body = new Payload(256);
        Envelope e = new Envelope(body);
        e.Reseal();
        System.Console.WriteLine($"contract={e.Describe()}");
    }

    public static CooperationContractDemo __CreateDefault()
    {
        var inst = new CooperationContractDemo();
        return inst;
    }
}
