using Standard;

namespace Snippet;

public class Greeter
{
    public string Name;

    public Greeter(string name)
    {
        this.Name = "";
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
        (int tag, object payload) envelope = (tag: 1, payload: new Snippet.Greeter("Mist"));
        System.Console.WriteLine(System.FormattableString.Invariant($"tag={envelope.tag}"));
        {
            if (envelope.payload is Snippet.Greeter g)
            {
                System.Console.WriteLine($"payload: {g.Greet()}");
            }
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
