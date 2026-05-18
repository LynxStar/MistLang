using System;

namespace Snippet;

public class AppConfig
{
    private string appName;

    private string version;

    public AppConfig(string appName, string version)
    {
        this.appName = appName;
        this.version = version;
    }

    public string Display()
    {
        return $"App: {appName} v{version}";
    }

    protected AppConfig()
    {
        this.appName = "";
        this.version = "";
    }

    public static AppConfig __CreateDefault()
    {
        var inst = new AppConfig();
        return inst;
    }
}
