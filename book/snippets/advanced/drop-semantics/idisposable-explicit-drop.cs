using System;

namespace Snippets;

public class Resource
{
    string Name;

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
            var r = new Resource("db-conn");
            try
            {
                System.Console.WriteLine($"using {r.Name}");
            }
            finally
            {
                r.Drop();
            }
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
