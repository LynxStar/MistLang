using System;

namespace HelloMist;

public class Greeter
{
    private string name;

    public Greeter(string name)
    {
        this.name = name;
    }

    public string Greet()
    {
        return $"Hello, {name}!";
    }

    protected Greeter()
    {
        this.name = "";
    }

    public static Greeter __CreateDefault()
    {
        var inst = new Greeter();
        return inst;
    }
}
