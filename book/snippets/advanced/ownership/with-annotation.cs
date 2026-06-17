using System;

namespace Snippet;

public class Resource
{
    public int Id;

    public int Priority;

    public Resource(int id, int priority)
    {
        this.Id = id;
        this.Priority = priority;
    }

    public int PeekId()
    {
        return this.Id;
    }

    public void Upgrade()
    {
        this.Priority = this.Priority + 1;
    }
}
public class WithAnnotationDemo
{
    public static void Run()
    {
        var res = new Resource(42, 1);
        System.Console.WriteLine($"peek={res.PeekId()}");
        res.Upgrade();
        System.Console.WriteLine($"after upgrade={res.PeekId()}");
    }

    public static WithAnnotationDemo __CreateDefault()
    {
        var inst = new WithAnnotationDemo();
        return inst;
    }
}
