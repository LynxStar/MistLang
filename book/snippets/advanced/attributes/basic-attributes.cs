using System;

namespace Snippet;

[System.SerializableAttribute]
class Config
{
    private string name;

    public Config(string name)
    {
        this.name = name;
    }
}

class Service
{
    [System.ObsoleteAttribute("use RunV2")]
    public void Run()
    {
        Console.WriteLine("Service.Run called");
    }

    public Service()
    {
    }

    public static Service __CreateDefault()
    {
        return new Service();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Service svc = new Snippet.Service();
        svc.Run();
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
