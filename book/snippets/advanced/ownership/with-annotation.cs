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
        Snippet.Resource res = new Snippet.Resource(42, 1);
        Console.WriteLine($"peek={res.PeekId()}");
        res.Upgrade();
        Console.WriteLine($"after upgrade={res.PeekId()}");
    }

    public WithAnnotationDemo()
    {
    }

    public static WithAnnotationDemo __CreateDefault()
    {
        return new WithAnnotationDemo();
    }
}
