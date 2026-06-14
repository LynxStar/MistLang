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
