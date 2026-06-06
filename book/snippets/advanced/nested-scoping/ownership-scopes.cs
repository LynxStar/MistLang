using System;

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

    protected Resource()
    {
        this.Id = 0;
    }

    public static Resource __CreateDefault()
    {
        var inst = new Resource();
        return inst;
    }
}
public class OwnershipScopeDemo
{
    public static void ReadOnly(Resource r)
    {
        Console.WriteLine($"frozen view: {r.Describe()}");
    }

    public static void Main(string[] args)
    {
        Resource res = new Resource(1);
        {
            ReadOnly(res);
        }
        res.Update(2);
        Console.WriteLine($"after update: {res.Describe()}");
    }

    public static OwnershipScopeDemo __CreateDefault()
    {
        var inst = new OwnershipScopeDemo();
        return inst;
    }
}
