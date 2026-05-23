using Standard;
using System;

namespace Demo;

public class Config
{
    public int Timeout { get; set; } = 30;

    public bool Verbose { get; set; } = false;

    public string Name { get; set; } = "default";

    protected Config()
    {
        this.Timeout = 0;
        this.Verbose = false;
        this.Name = "";
    }

    public static Config __CreateDefault()
    {
        var inst = new Config();
        return inst;
    }
}
