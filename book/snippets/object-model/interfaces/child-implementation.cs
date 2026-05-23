using System;

namespace Snippet;

public class Robot : IFullyDescribable
{
    private string model;

    private int version;

    public Robot(string model, int version)
    {
        this.model = model;
        this.version = version;
    }

    public string Describe()
    {
        return $"Robot: {model} v{version}";
    }

    public string DetailedDescribe()
    {
        return $"Robot model {model}, version {version}, fully operational";
    }

    protected Robot()
    {
        this.model = "";
        this.version = 0;
    }

    public static Robot __CreateDefault()
    {
        var inst = new Robot();
        return inst;
    }
}
