using System;

namespace Snippet;

[Serializable]
internal class Config
{
    string name;

    public Config(string name)
    {
        this.name = name;
    }
}
internal class Service
{
    [Obsolete("use RunV2")]
    public void Run()
    {
        Console.WriteLine("Service.Run called");
    }

    public static Service __CreateDefault()
    {
        var inst = new Service();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var svc = new Service();
        svc.Run();
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
