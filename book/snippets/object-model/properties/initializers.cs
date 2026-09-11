using System;

namespace Snippet;

public class Config
{
    public int Timeout { get; set; } = 30;

    public bool Verbose { get; set; } = false;

    public string Name { get; set; } = "default";

    public Config()
    {
    }

    public static Config __CreateDefault()
    {
        return new Config();
    }
}
