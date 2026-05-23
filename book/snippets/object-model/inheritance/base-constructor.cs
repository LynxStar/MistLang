using System;

namespace Snippet;

public class GuideDog : Dog
{
    private string handler;

    public GuideDog(string name, int age, string breed, string handler) : base(name, age, breed)
    {
        this.handler = handler;
    }

    public string GetHandler()
    {
        return handler;
    }

    protected GuideDog()
    {
        this.handler = "";
    }

    public static GuideDog __CreateDefault()
    {
        var inst = new GuideDog();
        return inst;
    }
}
