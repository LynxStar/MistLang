using System;

namespace Snippet;

public interface IDescribable
{
    string Describe();
}

interface IFullyDescribable : Snippet.IDescribable
{
    string DetailedDescribe();
}

public class Robot : Snippet.IFullyDescribable
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
}
