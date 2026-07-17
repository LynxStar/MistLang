using System;

namespace Snippet;

public sealed class Config
{
    private string key;

    private string value;

    public Config(string key, string value)
    {
        this.key = key;
        this.value = value;
    }

    public string GetKey()
    {
        return key;
    }

    public string GetValue()
    {
        return value;
    }
}
