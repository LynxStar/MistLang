using Standard;
using System;

namespace ScopingDemo;

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
