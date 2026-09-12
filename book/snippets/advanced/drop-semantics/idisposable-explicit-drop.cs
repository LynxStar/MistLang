using Standard;

namespace Snippets;

public class Resource
{
    public string Name;

    public Resource(string name)
    {
        this.Name = name;
    }

    public void Drop()
    {
        System.Console.WriteLine($"Resource({Name}).Drop()");
    }
}

public class Program
{
    public static void Main()
    {
        {
            var r = new Snippets.Resource("db-conn");
            MistContext.Push<Snippets.Resource>(r);
            try
            {
                System.Console.WriteLine($"using {r.Name}");
            }
            finally
            {
                MistContext.Pop<Snippets.Resource>();
                r.Drop();
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
