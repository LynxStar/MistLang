using Standard;
using System;

namespace UnknownDemo;

public class Greeter
{
    public string Name;

    public Greeter(string name)
    {
        this.Name = name;
    }

    public string Greet()
    {
        return $"Hello from {this.Name}";
    }

    protected Greeter()
    {
        this.Name = "";
    }

    public static Greeter __CreateDefault()
    {
        var inst = new Greeter();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        (int tag, object payload) envelope = (tag: 1, payload: new Greeter("Mist"));
        System.Console.WriteLine($"tag={envelope.tag}");
        if (envelope.payload is Greeter g)
        {
            System.Console.WriteLine($"payload: {g.Greet()}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
