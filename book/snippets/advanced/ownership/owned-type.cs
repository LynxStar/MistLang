using Standard;
using System;

namespace OwnershipSystem;

public class UniqueHandle
{
    int Id;

    int RefCount;

    public UniqueHandle(int id)
    {
        this.Id = id;
        this.RefCount = 1;
    }

    public int GetId()
    {
        return Id;
    }

    public void Touch()
    {
        this.RefCount = RefCount + 1;
    }

    public string Describe()
    {
        return $"UniqueHandle(Id={Id}, RefCount={RefCount})";
    }

    protected UniqueHandle()
    {
        this.Id = 0;
        this.RefCount = 0;
    }

    public static UniqueHandle __CreateDefault()
    {
        var inst = new UniqueHandle();
        return inst;
    }
}
public class OwnedTypeDemo
{
    public static void Run()
    {
        UniqueHandle h = new UniqueHandle(42);
        h.Touch();
        System.Console.WriteLine($"handle={h.Describe()}");
    }

    public static OwnedTypeDemo __CreateDefault()
    {
        var inst = new OwnedTypeDemo();
        return inst;
    }
}
