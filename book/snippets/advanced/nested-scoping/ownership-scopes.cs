using System;

namespace ScopingDemo;

public class Resource
{
    public int Id;
    public Resource(int id) { this.Id = id; }
    public string Describe() { return $"Resource({Id})"; }
    public void Update(int id) { this.Id = id; }
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
}
