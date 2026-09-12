using Standard;

namespace Snippet;

public class Resource
{
    public int Id;

    public Resource(int id)
    {
        this.Id = id;
    }

    public string Describe()
    {
        return $"Resource({Id})";
    }

    public void Update(int id)
    {
        this.Id = id;
    }
}

public class OwnershipScopeDemo
{
    public static void ReadOnly(Snippet.Resource r)
    {
        System.Console.WriteLine($"frozen view: {r.Describe()}");
    }

    public static void Main(string[] args)
    {
        Snippet.Resource res = new Snippet.Resource(1);
        {
            ReadOnly(res);
        }
        res.Update(2);
        System.Console.WriteLine($"after update: {res.Describe()}");
    }

    public OwnershipScopeDemo()
    {
    }

    public static OwnershipScopeDemo __CreateDefault()
    {
        return new OwnershipScopeDemo();
    }
}
