using System;

namespace Snippet;

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
}
public class Program
{
    public static void Main(string[] args)
    {
        (int tag, object payload) envelope = (tag: 1, payload: new Greeter("Mist"));
        Console.WriteLine($"tag={envelope.tag}");
        if (envelope.payload is Greeter g)
        {
            Console.WriteLine($"payload: {g.Greet()}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
